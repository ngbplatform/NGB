using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace NGB.Hosting.AspNetCore;

public static class DependencyInjection
{
    private const string CompletelyAllowedCorsPolicyName = "CompletelyAllowedCorsPolicy";

    public static void AddSerilog(this ConfigureHostBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));
    }

    public static IServiceCollection AddCompletelyAllowedCorsPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCors(options => options.AddPolicy(CompletelyAllowedCorsPolicyName, policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }));

        return services;
    }

    public static IApplicationBuilder UseCompletelyAllowedCorsPolicy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseCors(CompletelyAllowedCorsPolicyName);
    }

    public static IServiceCollection AddHealthCheckHttpClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddHttpClient("HealthCheckHttpClient")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
#if DEBUG
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
#endif
            });

        return services;
    }
}
