using System.Text;
using Microsoft.AspNetCore.Http;
using NGB.Api.Branding;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Hosting;

internal static class BackgroundJobsDashboardBranding
{
    private const string InlineStyleTagId = "ngb-background-jobs-dashboard-theme";
    private const string FaviconLinkTagId = "ngb-background-jobs-dashboard-favicon";
    private const string DashboardBrandSubtitleToken = "__NGB_DASHBOARD_BRAND_SUBTITLE__";

    public static string BuildInlineStyles(string contentRootPath, BackgroundJobsHostingOptions options)
    {
        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (string.IsNullOrWhiteSpace(contentRootPath))
            throw new NgbArgumentRequiredException(nameof(contentRootPath));

        if (options.DashboardStylesheetPaths.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var stylesheet in options.DashboardStylesheetPaths)
        {
            var resolvedPath = ResolveStylesheetPath(contentRootPath, stylesheet);
            builder.AppendLine(File.ReadAllText(resolvedPath));
        }

        return builder
            .ToString()
            .Replace(DashboardBrandSubtitleToken, EscapeCssContent(options.DashboardBrandSubtitle), StringComparison.Ordinal)
            .Trim();
    }

    public static bool IsDashboardRequest(HttpRequest request, BackgroundJobsHostingOptions options)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        return request.Path.StartsWithSegments(options.DashboardPath, StringComparison.OrdinalIgnoreCase);
    }

    public static string InjectBranding(string html, string inlineStyles, string faviconHref)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var builder = new StringBuilder();
        builder.Append(NgbStandaloneTheme.HeadScriptTag);

        if (!string.IsNullOrWhiteSpace(faviconHref))
            builder.Append($"<link id=\"{FaviconLinkTagId}\" rel=\"icon\" type=\"image/svg+xml\" href=\"{faviconHref}\">");

        if (!string.IsNullOrWhiteSpace(inlineStyles))
            builder.Append($"<style id=\"{InlineStyleTagId}\">{inlineStyles}</style>");

        var headMarkup = builder.ToString();
        var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        return headCloseIndex >= 0
            ? html.Insert(headCloseIndex, headMarkup)
            : headMarkup + html;
    }

    public static async Task InterceptHtmlAsync(
        HttpContext context,
        Func<Task> next,
        BackgroundJobsHostingOptions options,
        string inlineStyles,
        string faviconHref)
    {
        if (context is null)
            throw new NgbArgumentRequiredException(nameof(context));

        if (next is null)
            throw new NgbArgumentRequiredException(nameof(next));

        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (!IsDashboardRequest(context.Request, options)
            || (string.IsNullOrWhiteSpace(inlineStyles) && string.IsNullOrWhiteSpace(faviconHref)))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();

            buffer.Position = 0;

            if (context.Response.StatusCode != StatusCodes.Status200OK || !IsHtmlResponse(context.Response))
            {
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var html = await reader.ReadToEndAsync();
            var injected = InjectBranding(html, inlineStyles, faviconHref);
            var payload = Encoding.UTF8.GetBytes(injected);

            context.Response.Body = originalBody;
            context.Response.ContentLength = payload.Length;
            await originalBody.WriteAsync(payload);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsHtmlResponse(HttpResponse response)
        => response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;

    private static string ResolveStylesheetPath(string contentRootPath, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new NgbArgumentRequiredException(nameof(configuredPath));

        var normalizedPath = configuredPath.Trim();

        if (Path.IsPathRooted(normalizedPath) && File.Exists(normalizedPath))
            return normalizedPath;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, normalizedPath),
            Path.Combine(contentRootPath, normalizedPath)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new NgbConfigurationViolationException(
            $"Background jobs dashboard stylesheet '{configuredPath}' was not found.",
            new Dictionary<string, object?>
            {
                ["configuredPath"] = configuredPath,
                ["candidates"] = candidates
            });
    }

    private static string EscapeCssContent(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
