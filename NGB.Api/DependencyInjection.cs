using System.Text.Json.Serialization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerUI;
using NGB.Api.CurrentUser;
using NGB.Api.Models;
using NGB.Api.Sso;
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
            c.TagActionsBy(api =>
            {
                if (api.GroupName != null)
                    return [api.GroupName];

                if (api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                    return [controllerActionDescriptor.ControllerName];

                throw new NgbInvariantViolationException("Unable to determine tag for endpoint.");
            });
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
        var genericName = genericRoot.FullName ?? genericRoot.Name;
        var tickIndex = genericName.IndexOf('`');
        if (tickIndex >= 0)
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
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
    }

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
