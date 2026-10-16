using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using NGB.Hosting.AspNetCore.Identity;

namespace NGB.Mcp.AspNetCore;

public static class DependencyInjection
{
    internal const string Scheme = "NgbMcp";
    internal const string BearerScheme = "NgbMcp.Bearer";
    private const string Policy = "NgbMcp.CurrentUser";

    public static IServiceCollection AddNgbMcp(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Mcp").Get<NgbMcpOptions>() ?? new();
        services.AddOptions<NgbMcpOptions>().Bind(configuration.GetSection("Mcp"));
        if (!settings.Enabled)
            return services;

        Validate(settings);

        if (settings.PublicUiOrigin is not null)
        {
            var links = new ConfiguredMcpNgbLinkResolver(new Uri(settings.PublicUiOrigin), settings.UiRoutes, settings.AllowLocalhostHttp);
            services.TryAddSingleton<IMcpNgbLinkResolver>(links);
        }

        services.AddNgbMcpReadTools(options => configuration.GetSection("Mcp:Reads").Bind(options));
        services.TryAddSingleton<McpRequestBudget>();
        services.AddHostedService<McpStartupValidator>();
        services.AddHttpContextAccessor();

        services.AddAuthentication().AddJwtBearer(BearerScheme).AddMcp(Scheme, "NGB MCP", options =>
        {
            options.ForwardAuthenticate = BearerScheme;
            options.ResourceMetadataUri = new Uri(new Uri(settings.Resource), "/.well-known/oauth-protected-resource" + new Uri(settings.Resource).AbsolutePath);
        });

        services.AddOptions<McpAuthenticationOptions>(Scheme).Configure<KeycloakSettings>((options, keycloak) =>
        {
            if (!IsSecureAbsoluteUri(keycloak.Issuer, settings.AllowLocalhostHttp))
                throw new InvalidOperationException("MCP requires a secure existing Keycloak issuer.");

            options.ResourceMetadata = new()
            {
                Resource = settings.Resource,
                AuthorizationServers = [keycloak.Issuer],
                ResourceName = "NGB read-only MCP"
            };
        });

        services.AddOptions<JwtBearerOptions>(BearerScheme).Configure<IServiceProvider>((options, provider) =>
        {
            var existing = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(settings.AuthenticationScheme);
            if (existing.EventsType is not null)
                throw new InvalidOperationException("MCP requires the existing bearer scheme to configure JwtBearerEvents directly.");

            options.Authority = existing.Authority;
            options.MetadataAddress = existing.MetadataAddress;
            options.RequireHttpsMetadata = existing.RequireHttpsMetadata;
            options.Configuration = existing.Configuration;
            options.ConfigurationManager = existing.ConfigurationManager;
            options.Backchannel = existing.Backchannel;
            options.BackchannelHttpHandler = existing.BackchannelHttpHandler;
            options.BackchannelTimeout = existing.BackchannelTimeout;
            options.RefreshOnIssuerKeyNotFound = existing.RefreshOnIssuerKeyNotFound;
            options.MapInboundClaims = existing.MapInboundClaims;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = existing.TokenValidationParameters.Clone();
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.RequireSignedTokens = true;
            options.TokenValidationParameters.RequireExpirationTime = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.RequireAudience = true;
            var audience = string.IsNullOrWhiteSpace(settings.Audience) ? settings.Resource : settings.Audience;
            options.TokenValidationParameters.AudienceValidator = (audiences, _, _) => audiences.Contains(audience, StringComparer.Ordinal);
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    await existing.Events.OnTokenValidated(context);
                    if (context.Principal is null || !IsUserResourceToken(context.Principal, settings))
                        context.Fail("The token is not valid for this MCP resource and user flow.");
                }
            };
        });

        services.AddCors(options => options.AddPolicy(Policy, policy => policy
            .WithOrigins(settings.AllowedOrigins.Append(new Uri(settings.Resource).GetLeftPart(UriPartial.Authority)).Distinct().ToArray())
            .WithMethods("POST", "GET")
            .WithHeaders("Authorization", "Content-Type", "MCP-Protocol-Version", "Mcp-Method", "Mcp-Name")
            .WithExposedHeaders("WWW-Authenticate", "MCP-Protocol-Version")));

        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => IsUserResourceToken(context.User, settings));
        }));

        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler(McpProtocolHandlers.ListAsync)
            .WithCallToolHandler(McpProtocolHandlers.CallAsync);

        return services;
    }

    public static IEndpointRouteBuilder MapNgbMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        var settings = endpoints.ServiceProvider.GetRequiredService<IOptions<NgbMcpOptions>>().Value;
        if (!settings.Enabled)
            return endpoints;

        Validate(settings);
        if (!string.Equals(new Uri(settings.Resource).AbsolutePath, pattern, StringComparison.Ordinal))
            throw new InvalidOperationException("MCP endpoint path must match the configured canonical resource URI.");

        // Validate metadata immediately. The startup service validates scoped dependencies asynchronously.
        _ = endpoints.ServiceProvider.GetRequiredService<McpReadToolRegistry>();
        var group = endpoints.MapGroup(string.Empty);
        group.AddEndpointFilter<McpEndpointFilter>();
        group.MapMcp(pattern).RequireAuthorization(Policy).RequireCors(Policy);

        return endpoints;
    }

    private static bool IsUserResourceToken(ClaimsPrincipal principal, NgbMcpOptions settings)
    {
        var subject = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var audience = string.IsNullOrWhiteSpace(settings.Audience) ? settings.Resource : settings.Audience;

        return !string.IsNullOrWhiteSpace(subject)
            && principal.FindAll("aud").Any(claim => string.Equals(claim.Value, audience, StringComparison.Ordinal))
            && long.TryParse(principal.FindFirst("auth_time")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var authenticatedAt)
            && authenticatedAt > 0
            && !principal.HasClaim("grant_type", "client_credentials")
            && !(principal.FindFirst("preferred_username")?.Value.StartsWith("service-account-", StringComparison.Ordinal) ?? false);
    }

    private static void Validate(NgbMcpOptions options)
    {
        if (options.MaxRequestBytes <= 0)
            throw new InvalidOperationException($"{nameof(NgbMcpOptions)}.{nameof(options.MaxRequestBytes)} must be positive.");

        if (!IsSecureAbsoluteUri(options.Resource, options.AllowLocalhostHttp)
            || new Uri(options.Resource).AbsolutePath == "/"
            || string.IsNullOrWhiteSpace(options.AuthenticationScheme)
            || options.AuthenticationScheme == Scheme
            || options.AuthenticationScheme == BearerScheme
            || options.ConcurrentRequests is < 1 or > 256
            || options.RequestsPerMinute is < 1 or > 100_000
            || options.RequestTimeoutSeconds is < 1 or > 300
            || options.AllowedOrigins.Any(origin => !IsSecureAbsoluteUri(origin, options.AllowLocalhostHttp)
                || new Uri(origin).GetLeftPart(UriPartial.Authority) != origin))
        {
            throw new InvalidOperationException("Invalid MCP resource, origin or request-limit configuration.");
        }
    }

    private static bool IsSecureAbsoluteUri(string value, bool allowLocalhostHttp)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && (uri.Scheme == "https" || allowLocalhostHttp && uri is { Scheme: "http", IsLoopback: true });
}
