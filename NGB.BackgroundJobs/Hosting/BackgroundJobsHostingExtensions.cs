using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.Branding;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Hosting.AspNetCore.Health;
using NGB.Hosting.AspNetCore.Identity;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

public static class BackgroundJobsHostingExtensions
{
    public static BackgroundJobsHostingBootstrap AddNgbBackgroundJobs(
        this WebApplicationBuilder builder,
        BackgroundJobStorageFactory jobStorageFactory,
        Action<BackgroundJobsHostingOptions>? configure = null)
    {
        if (builder is null)
            throw new NgbArgumentRequiredException(nameof(builder));

        if (jobStorageFactory is null)
            throw new NgbArgumentRequiredException(nameof(jobStorageFactory));

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
        var jobStorage = jobStorageFactory(
            hangfireConnectionString,
            options.HangfireStorageNamespace,
            options.PrepareHangfireSchemaIfNecessary)
            ?? throw new NgbConfigurationViolationException("The background-job storage factory returned null.");

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
            .AddKeycloak()
            .AddHangfire(
                setup => setup.MaximumJobsFailed = options.HangfireHealthCheckMaximumFailedJobs,
                name: options.HangfireHealthCheckName);

        builder.Services.AddPlatformBackgroundJobSchedulesFromConfiguration(
            builder.Configuration,
            options.BackgroundJobsSectionName);

        builder.Services.AddPlatformBackgroundJobsHangfire(jobStorage, hangfireOptions =>
        {
            hangfireOptions.ConnectionString = hangfireConnectionString;
            hangfireOptions.StorageNamespace = options.HangfireStorageNamespace;
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
            .UseMiddleware<BackgroundJobsDashboardBrandingMiddleware>(
                options, inlineDashboardStyles, NgbBrandingAssets.DefaultFaviconHref);
    }

    public static WebApplication MapNgbBackgroundJobs(this WebApplication app)
    {
        if (app is null)
            throw new NgbArgumentRequiredException(nameof(app));

        var options = app.Services.GetRequiredService<IOptions<BackgroundJobsHostingOptions>>().Value;

        app.MapHealthChecks(options.HealthPath, new HealthCheckOptions
        {
            ResponseWriter = NgbHealthCheckResponseWriter.WriteAsync
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
