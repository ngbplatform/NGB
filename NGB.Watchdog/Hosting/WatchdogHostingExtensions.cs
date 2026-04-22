using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using NGB.Api;
using NGB.Api.Branding;
using NGB.Api.Sso;
using NGB.Tools.Exceptions;
using NGB.Watchdog.HealthChecks;

namespace NGB.Watchdog.Hosting;

public static class WatchdogHostingExtensions
{
    public static IHealthChecksBuilder AddNgbWatchdog(this WebApplicationBuilder builder, string? pageTittle = null)
    {
        return builder.AddNgbWatchdog(options =>
            {
                if (pageTittle != null)
                    options.PageTitle = pageTittle;

                options.AddCustomStylesheet("dashboard.css");
            })
            .AddWebClient();
    }
    
    public static IHealthChecksBuilder AddNgbWatchdog(this WebApplicationBuilder builder,
        Action<WatchdogOptions>? configure)
    {
        if (builder is null)
            throw new NgbArgumentRequiredException(nameof(builder));

        var options = new WatchdogOptions();
        configure?.Invoke(options);
        options.ValidateAndNormalize();

        builder.Host.AddSerilog();

        var healthChecks = builder.Services.AddHealthChecks();

        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHttpClient();
        builder.Services.AddHealthCheckHttpClient();
        builder.Services.AddCompletelyAllowedCorsPolicy();
        builder.Services.AddKeycloakForAdminConsole(builder.Configuration);

        builder.Services
            .AddHealthChecksUI(uiOptions =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    uiOptions.UseApiEndpointHttpMessageHandler(_ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    });
                }
            })
            .AddInMemoryStorage();

        return healthChecks;
    }

    public static WebApplication UseNgbWatchdog(this WebApplication app)
    {
        if (app is null)
            throw new NgbArgumentRequiredException(nameof(app));

        var options = app.GetRequiredService<IOptions<WatchdogOptions>>().Value;

        return (WebApplication)app
            .UseSerilogRequestLogging()
            .UseHttpsRedirection()
            .UseCompletelyAllowedCorsPolicy()
            .UseAuthentication()
            .UseAuthorization()
            .Use(async (context, next) => await WatchdogUiBranding.InterceptHtmlAsync(context, next, options, NgbBrandingAssets.DefaultFaviconHref));
    }

    public static WebApplication MapNgbWatchdog(this WebApplication app)
    {
        if (app is null)
            throw new NgbArgumentRequiredException(nameof(app));

        var options = app.GetRequiredService<IOptions<WatchdogOptions>>().Value;

        app.MapHealthChecks(options.HealthPath, new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        var uiEndpoint = app.MapHealthChecksUI(uiOptions =>
        {
            uiOptions.AsideMenuOpened = options.AsideMenuOpened;
            uiOptions.PageTitle = options.PageTitle;
            uiOptions.UIPath = options.UiPath;
            uiOptions.ApiPath = options.ApiPath;

            foreach (var stylesheet in options.CustomStylesheets)
            {
                uiOptions.AddCustomStylesheet(ResolveStylesheetPath(app, stylesheet));
            }
        });

        if (options.RequireAuthorization)
            uiEndpoint.GlobalCookieRequireAuthorization();

        if (options.MapAccountEndpoints)
            app.MapAccountEndpoints(options.UiPath);

        return app;
    }

    private static T GetRequiredService<T>(this WebApplication app)
        where T : notnull
        => app.Services.GetRequiredService<T>();

    private static string ResolveStylesheetPath(WebApplication app, string stylesheetPath)
    {
        if (string.IsNullOrWhiteSpace(stylesheetPath))
            throw new NgbArgumentRequiredException(nameof(stylesheetPath));

        if (Path.IsPathRooted(stylesheetPath))
        {
            EnsureStylesheetExists(stylesheetPath, stylesheetPath, app);
            return stylesheetPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, stylesheetPath),
            Path.Combine(app.Environment.ContentRootPath, stylesheetPath)
        };

        var resolvedPath = candidates.FirstOrDefault(File.Exists);
        if (resolvedPath is null)
            throw BuildMissingStylesheetException(stylesheetPath, candidates, app);

        return resolvedPath;
    }

    private static void EnsureStylesheetExists(string resolvedPath, string configuredPath, WebApplication app)
    {
        if (!File.Exists(resolvedPath))
            throw BuildMissingStylesheetException(configuredPath, [resolvedPath], app);
    }

    private static NgbConfigurationViolationException BuildMissingStylesheetException(
        string configuredPath,
        IEnumerable<string> candidates,
        WebApplication app)
    {
        var inspectedPaths = candidates.ToArray();

        return new NgbConfigurationViolationException(
            $"Watchdog stylesheet '{configuredPath}' was not found.",
            new Dictionary<string, object?>
            {
                ["configuredPath"] = configuredPath,
                ["appBaseDirectory"] = AppContext.BaseDirectory,
                ["contentRootPath"] = app.Environment.ContentRootPath,
                ["candidatePaths"] = inspectedPaths
            });
    }
}
