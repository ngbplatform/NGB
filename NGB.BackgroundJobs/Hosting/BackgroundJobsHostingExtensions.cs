using Hangfire;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using NGB.Api;
using NGB.Api.Branding;
using NGB.Api.GlobalErrorHandling;
using NGB.Api.Sso;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

public static class BackgroundJobsHostingExtensions
{
    public static BackgroundJobsHostingBootstrap AddNgbBackgroundJobs(this WebApplicationBuilder builder,
        Action<BackgroundJobsHostingOptions>? configure = null)
    {
        if (builder is null)
            throw new NgbArgumentRequiredException(nameof(builder));

        var options = new BackgroundJobsHostingOptions();
        configure?.Invoke(options);
        options.ValidateAndNormalize();

        var applicationConnectionString = GetRequiredConnectionString(
            builder.Configuration,
            options.ApplicationConnectionStringName);

        var hangfireConnectionString = ResolveHangfireConnectionString(
            builder.Configuration,
            options,
            applicationConnectionString);

        builder.Host.AddSerilog();

        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddGlobalErrorHandling();
        builder.Services.AddCompletelyAllowedCorsPolicy();
        builder.Services.AddKeycloakForAdminConsole(builder.Configuration, auth =>
        {
            auth.CallbackPath = options.AdminConsoleCallbackPath ?? "/signin-oidc";
            auth.PublicOrigin = options.AdminConsolePublicOrigin;
        });

        builder.Services.AddHealthChecks()
            .AddNpgSql(applicationConnectionString, name: options.PostgresHealthCheckName)
            .AddKeycloak()
            .AddHangfire(
                setup => setup.MaximumJobsFailed = options.HangfireHealthCheckMaximumFailedJobs,
                name: options.HangfireHealthCheckName);

        builder.Services.AddPlatformBackgroundJobSchedulesFromConfiguration(
            builder.Configuration,
            options.BackgroundJobsSectionName);

        builder.Services.AddPlatformBackgroundJobsHangfire(hangfireOptions =>
        {
            hangfireOptions.ConnectionString = hangfireConnectionString;
            hangfireOptions.PrepareSchemaIfNecessary = options.PrepareHangfireSchemaIfNecessary;
            hangfireOptions.WorkerCount = options.WorkerCount;
            hangfireOptions.DistributedLockTimeoutSeconds = options.DistributedLockTimeoutSeconds;
            hangfireOptions.ServerName = options.ServerName;
            hangfireOptions.Queues = options.Queues.ToArray();
        });

        return new BackgroundJobsHostingBootstrap(options, applicationConnectionString, hangfireConnectionString);
    }

    public static WebApplication UseNgbBackgroundJobs(this WebApplication app)
    {
        if (app is null)
            throw new NgbArgumentRequiredException(nameof(app));

        var options = app.Services.GetRequiredService<IOptions<BackgroundJobsHostingOptions>>().Value;
        var inlineDashboardStyles = BackgroundJobsDashboardBranding.BuildInlineStyles(app.Environment.ContentRootPath, options);

        return (WebApplication)app
            .UseSerilogRequestLogging()
            .UseHttpsRedirection()
            .UseCompletelyAllowedCorsPolicy()
            .UseExceptionHandler()
            .UseCustomForwardedHeaders()
            .UseAuthentication()
            .UseAuthorization()
            .Use(async (context, next) =>
                await BackgroundJobsDashboardBranding.InterceptHtmlAsync(context, next, options, inlineDashboardStyles, NgbBrandingAssets.DefaultFaviconHref));
    }

    public static WebApplication MapNgbBackgroundJobs(this WebApplication app)
    {
        if (app is null)
            throw new NgbArgumentRequiredException(nameof(app));

        var options = app.Services.GetRequiredService<IOptions<BackgroundJobsHostingOptions>>().Value;

        app.MapHealthChecks(options.HealthPath, new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        var dashboard = app.MapHangfireDashboard(options.DashboardPath, new DashboardOptions
        {
            AppPath = null,
            DashboardTitle = options.DashboardTitle,
            Authorization = options.RequireDashboardAuthorization
                ? [new HangfireDashboardAuthorizationFilter()]
                : []
        });

        if (options.RequireDashboardAuthorization)
            dashboard.GlobalCookieRequireAuthorization();

        if (options.MapAccountEndpoints)
            app.MapAccountEndpoints(options.DashboardPath);

        return app;
    }

    private static string GetRequiredConnectionString(IConfiguration configuration, string connectionStringName)
    {
        if (configuration is null)
            throw new NgbArgumentRequiredException(nameof(configuration));

        if (string.IsNullOrWhiteSpace(connectionStringName))
            throw new NgbArgumentRequiredException(nameof(connectionStringName));

        var value = configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new NgbConfigurationViolationException(
            $"Please provide PostgreSQL connection string in 'ConnectionStrings:{connectionStringName}'.",
            new Dictionary<string, object?>
            {
                ["connectionStringName"] = connectionStringName
            });
    }

    private static string ResolveHangfireConnectionString(
        IConfiguration configuration,
        BackgroundJobsHostingOptions options,
        string applicationConnectionString)
    {
        var configured = configuration.GetConnectionString(options.HangfireConnectionStringName);
        return string.IsNullOrWhiteSpace(configured)
            ? applicationConnectionString
            : configured.Trim();
    }
}
