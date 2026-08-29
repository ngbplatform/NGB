using System.Text.Json.Serialization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerUI;
using NGB.Api.CurrentUser;
using NGB.Api.Models;
using NGB.Api.Sso;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Api;

public static class DependencyInjection
{
    private const string CompletelyAllowedCorsPolicyName = "CompletelyAllowedCorsPolicy";

    public static void AddSerilog(this ConfigureHostBuilder host)
    {
        host.UseSerilog((ctx, cfg)
            => cfg.ReadFrom.Configuration(ctx.Configuration));
    }

    #region IServiceCollection

    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration,
        string projectName)
    {
        services.TryAddSingleton(TimeProvider.System);

        services
            .AddCompletelyAllowedCorsPolicy()
            .AddKeycloak(configuration)
            .AddKeycloakAdminClient(configuration)
            .AddSwagger(projectName)
            .AddCurrentUserInfrastructure();

        return services;
    }

    public static IServiceCollection AddCurrentUserInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        
        services.RemoveAll<ICurrentActorContext>();
        services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();

        return services;
    }

    public static IServiceCollection AddKeycloakAdminClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(nameof(KeycloakAdminClientSettings));
        var settings = section.Get<KeycloakAdminClientSettings>() ?? new KeycloakAdminClientSettings();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(settings);
        services.TryAddSingleton(new KeycloakApiClientSettings(
            settings.BaseUrl,
            settings.Realm,
            settings.ClientId,
            settings.ClientSecret));

        ValidateKeycloakClientSettings(settings);

        services
            .AddHttpClient(KeycloakHttpClientNames.Token, static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(options => ConfigureKeycloakResilience(options, settings, retryUnsafeMethods: true));

        services.TryAddSingleton<TokenCacheService>();
        services.TryAddSingleton<KeycloakUserLookupCache>();

        services
            .AddHttpClient<IIdentityProviderUserAdminClient, KeycloakAdminClient>(
                static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(options => ConfigureKeycloakResilience(options, settings, retryUnsafeMethods: false));

        return services;
    }

    private static void ConfigureKeycloakResilience(
        HttpStandardResilienceOptions options,
        KeycloakAdminClientSettings settings,
        bool retryUnsafeMethods)
    {
        options.TotalRequestTimeout.Timeout = settings.TotalRequestTimeout;
        options.AttemptTimeout.Timeout = settings.AttemptTimeout;
        options.Retry.MaxRetryAttempts = 2;

        if (!retryUnsafeMethods)
            options.Retry.DisableForUnsafeHttpMethods();
    }

    private static void ValidateKeycloakClientSettings(KeycloakAdminClientSettings settings)
    {
        if (settings.TotalRequestTimeout <= TimeSpan.Zero)
            throw new NgbConfigurationViolationException("Keycloak total request timeout must be positive.");

        if (settings.AttemptTimeout <= TimeSpan.Zero || settings.AttemptTimeout > settings.TotalRequestTimeout)
            throw new NgbConfigurationViolationException("Keycloak attempt timeout must be positive and not exceed the total request timeout.");

        if (settings.UserLookupCacheTtl <= TimeSpan.Zero || settings.MissingUserCacheTtl <= TimeSpan.Zero)
            throw new NgbConfigurationViolationException("Keycloak user lookup cache TTL values must be positive.");

        if (settings.MaxCachedUserLookups is < 100 or > 200_000)
            throw new NgbConfigurationViolationException("Keycloak user lookup cache size must be between 100 and 200000.");
    }

    public static IServiceCollection AddExternalLinks(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = ConfigurationTools.GetSettings<ExternalLinksSettings>(configuration);
        services.AddSingleton(settings);
        
        return services;
    }
    
    public static IServiceCollection AddControllersApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddApplicationPart(typeof(DependencyInjection).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return services;
    }

    public static IServiceCollection AddCompletelyAllowedCorsPolicy(this IServiceCollection services)
    {
        services.AddCors(o => o.AddPolicy(CompletelyAllowedCorsPolicyName, b =>
        {
            b.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }));

        return services;
    }

    /// <summary>
    /// Add Swagger
    /// </summary>
    /// <example>
    /// LOCAL URL: https://localhost:7070/swagger/index.html
    /// </example>
    /// <param name="services">Target: IServiceCollection</param>
    /// <param name="projectName">Project Name</param>
    /// <param name="version">Version API ('v1' by default)</param>
    /// <returns></returns>
    /// <exception cref="NgbInvariantViolationException"></exception>
    private static IServiceCollection AddSwagger(this IServiceCollection services, string projectName)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = projectName });
            c.CustomSchemaIds(BuildSwaggerSchemaId);
            c.DescribeAllParametersInCamelCase();
            c.UseInlineDefinitionsForEnums();
            c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme (Example: 'Bearer 12345abcdef')",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = JwtBearerDefaults.AuthenticationScheme
            });
            c.TagActionsBy(ResolveSwaggerTags);
            c.DocInclusionPredicate((name, api) => true);

            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme),
                    []
                }
            });
        });

        return services;
    }

    private static string BuildSwaggerSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return SanitizeSwaggerSchemaId(type.FullName ?? type.Name);

        var genericRoot = type.GetGenericTypeDefinition();
        var genericName = genericRoot.FullName!;
        var tickIndex = genericName.IndexOf('`');
        genericName = genericName[..tickIndex];

        var args = string.Join("_", type.GetGenericArguments().Select(BuildSwaggerSchemaId));
        return SanitizeSwaggerSchemaId($"{genericName}_{args}");
    }

    private static string SanitizeSwaggerSchemaId(string value)
        => value
            .Replace('.', '_')
            .Replace('+', '_')
            .Replace('[', '_')
            .Replace(']', '_')
            .Replace(',', '_');

    private static IList<string> ResolveSwaggerTags(ApiDescription api)
    {
        if (api.GroupName is not null)
            return [api.GroupName];

        if (api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
            return [controllerActionDescriptor.ControllerName];

        throw new NgbInvariantViolationException("Unable to determine tag for endpoint.");
    }

    #endregion

    #region IApplicationBuilder

    public static IApplicationBuilder UseCompletelyAllowedCorsPolicy(this IApplicationBuilder app)
    {
        return app.UseCors(CompletelyAllowedCorsPolicyName);
    }

    public static IApplicationBuilder UseSwagger(this IApplicationBuilder app, string projectName)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocExpansion(DocExpansion.None);
            options.DocumentTitle = projectName;
        });

        return app;
    }

    public static RouteHandlerBuilder MapRootEndpoint(this IEndpointRouteBuilder endpoints,
        string context = "Web Application has been started.")
    {
        // NOTE: Swagger doesn't work!

        return endpoints.MapGet("/", () => context);
    }

    #endregion

    #region HealthHeckers

    public static IServiceCollection AddHealthCheckHttpClient(this IServiceCollection services)
    {
        services
            .AddHttpClient("HealthCheckHttpClient")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
#if DEBUG // Disable SSL Validation (Development Only)
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
#endif
            });

        return services;
    }

    public static IApplicationBuilder UseHealthChecks(this IApplicationBuilder app, string path = "/health")
    {
        return app.UseHealthChecks(path, new HealthCheckOptions
        {
            Predicate = IncludeEveryHealthCheck,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
    }

    private static bool IncludeEveryHealthCheck(HealthCheckRegistration _) => true;

    public static IHealthChecksBuilder AddWebApplication(this IHealthChecksBuilder builder,
        string name = "Web Application")
    {
        return builder.AddCheck(name, () => HealthCheckResult.Healthy());
    }

    public static IHealthChecksBuilder AddPostgres(this IHealthChecksBuilder builder,
        IConfiguration configuration,
        string name = "PostgreSQL Server")
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;
        return builder.AddNpgSql(connectionString, name: name);
    }

    #endregion
}
