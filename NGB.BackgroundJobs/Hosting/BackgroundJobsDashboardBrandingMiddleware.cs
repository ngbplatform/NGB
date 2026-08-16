using Microsoft.AspNetCore.Http;

namespace NGB.BackgroundJobs.Hosting;

internal sealed class BackgroundJobsDashboardBrandingMiddleware(
    RequestDelegate next,
    BackgroundJobsHostingOptions options,
    string inlineStyles,
    string faviconHref)
{
    public Task InvokeAsync(HttpContext context) =>
        BackgroundJobsDashboardBranding.InterceptHtmlAsync(
            context, next, options, inlineStyles, faviconHref);
}
