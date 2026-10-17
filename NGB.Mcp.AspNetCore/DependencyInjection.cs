using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using NGB.Core.Features;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Runtime.DependencyInjection;
using NGB.Tools.Exceptions;
using NGB.Tools.Http;

namespace NGB.Mcp.AspNetCore;

public static class DependencyInjection
{
    internal const string Scheme = "NgbMcp";
    public const string CurrentUserPolicyName = "NgbMcp.CurrentUser";
    private const string Policy = CurrentUserPolicyName;

    /// <summary>Gets the canonical endpoint after MapNgbMcp has mapped its route.</summary>
    public static Uri GetNgbMcpEndpoint(this IServiceProvider services)
        => services.GetRequiredService<McpEndpointAddress>().Resource;

    public static IServiceCollection AddNgbMcp(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetSection("Mcp:Enabled").Exists())
            throw new NgbConfigurationViolationException("Mcp:Enabled has been replaced by FeatureManagement:Mcp. Configure only the registered feature flag.");

        if (configuration.GetSection("Mcp:Audience").Exists())
            throw new NgbConfigurationViolationException("Mcp:Audience is no longer supported. MCP uses the existing API bearer authentication scheme and its token validation. Remove Mcp:Audience.");

        services.AddNgbFeatureManagement(configuration);
        if (!configuration.GetValue<bool>($"FeatureManagement:{NgbFeatures.Mcp}"))
            return services;

        var settings = configuration.GetSection("Mcp").Get<NgbMcpOptions>() ?? new();
        services.AddOptions<NgbMcpOptions>().Bind(configuration.GetSection("Mcp"));

        Validate(settings);
        var hosting = configuration.GetSection(NgbHostingOptions.ConfigurationSection).Get<NgbHostingOptions>() ?? new();
        var endpoint = new McpEndpointAddress(settings, hosting);
        services.AddOptions<NgbHostingOptions>().Bind(configuration.GetSection(NgbHostingOptions.ConfigurationSection));
        services.AddSingleton(endpoint);

        if (settings.PublicUiOrigin is not null)
        {
            var links = new ConfiguredMcpNgbLinkResolver(new Uri(settings.PublicUiOrigin), settings.UiRoutes, settings.AllowLocalhostHttp);
            services.TryAddSingleton<IMcpNgbLinkResolver>(links);
        }

        services.AddNgbMcpReadTools(options => configuration.GetSection("Mcp:Reads").Bind(options));
        services.TryAddSingleton<McpRequestBudget>();
        services.AddHostedService<McpStartupValidator>();
        services.AddHttpContextAccessor();

        services.AddAuthentication().AddMcp(Scheme, "NGB MCP", options =>
        {
            options.ForwardAuthenticate = settings.AuthenticationScheme;
            options.ResourceMetadataUri = endpoint.MetadataUri;
        });

        services.AddOptions<McpAuthenticationOptions>(Scheme).Configure<KeycloakSettings>((options, keycloak) =>
        {
            if (!HttpEndpointUri.IsSecureAbsolute(keycloak.Issuer, settings.AllowLocalhostHttp))
                throw new InvalidOperationException("MCP requires a secure existing Keycloak issuer.");

            options.ResourceMetadata = new()
            {
                Resource = endpoint.Resource.AbsoluteUri,
                AuthorizationServers = [keycloak.Issuer],
                ResourceName = "NGB read-only MCP"
            };
        });

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure(options => options.AddPolicy(Policy, policy => policy
            .WithOrigins(settings.AllowedOrigins.Append(endpoint.Resource.GetLeftPart(UriPartial.Authority)).Distinct().ToArray())
            .WithMethods("POST", "GET")
            .WithHeaders("Authorization", "Content-Type", "MCP-Protocol-Version", "Mcp-Method", "Mcp-Name")
            .WithExposedHeaders("WWW-Authenticate", "MCP-Protocol-Version")));

        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => context.Resource is HttpContext http
                && !http.Request.Query.ContainsKey("access_token")
                && AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var authorization)
                && string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(authorization.Parameter)
                && IsUserAccessToken(context.User));
        }));

        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler(McpProtocolHandlers.ListAsync)
            .WithCallToolHandler(McpProtocolHandlers.CallAsync);

        return services;
    }

    public static IEndpointRouteBuilder MapNgbMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        // Registration captures the startup feature state; configuration reloads cannot remap the endpoint.
        var endpoint = endpoints.ServiceProvider.GetService<McpEndpointAddress>();
        if (endpoint is null)
            return endpoints;

        var settings = endpoints.ServiceProvider.GetRequiredService<IOptions<NgbMcpOptions>>().Value;
        Validate(settings);
        endpoint.Map(pattern);

        // Validate metadata immediately. The startup service validates scoped dependencies asynchronously.
        _ = endpoints.ServiceProvider.GetRequiredService<McpReadToolRegistry>();
        var group = endpoints.MapGroup(string.Empty);
        group.AddEndpointFilter<McpEndpointFilter>();
        group.MapMcp(pattern).RequireAuthorization(Policy).RequireCors(Policy);

        return endpoints;
    }

    private static bool IsUserAccessToken(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return !string.IsNullOrWhiteSpace(subject)
            && principal.HasClaim("typ", "Bearer")
            && long.TryParse(principal.FindFirst("auth_time")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var authenticatedAt)
            && authenticatedAt > 0
            && !principal.HasClaim("grant_type", "client_credentials")
            && !(principal.FindFirst("preferred_username")?.Value.StartsWith("service-account-", StringComparison.Ordinal) ?? false);
    }

    private static void Validate(NgbMcpOptions options)
    {
        if (options.MaxRequestBytes <= 0)
            throw new InvalidOperationException($"{nameof(NgbMcpOptions)}.{nameof(options.MaxRequestBytes)} must be positive.");

        if (string.IsNullOrWhiteSpace(options.AuthenticationScheme)
            || options.AuthenticationScheme == Scheme
            || options.ConcurrentRequests is < 1 or > 256
            || options.RequestsPerMinute is < 1 or > 100_000
            || options.RequestTimeoutSeconds is < 1 or > 300
            || options.AllowedOrigins.Any(origin => !HttpEndpointUri.IsSecureAbsolute(origin, options.AllowLocalhostHttp)
                || new Uri(origin).GetLeftPart(UriPartial.Authority) != origin))
        {
            throw new InvalidOperationException("Invalid MCP resource, origin or request-limit configuration.");
        }
    }
}
