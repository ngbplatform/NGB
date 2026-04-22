using System.Text;
using Microsoft.AspNetCore.Http;
using NGB.Api.Branding;
using NGB.Tools.Exceptions;

namespace NGB.Watchdog.Hosting;

internal static class WatchdogUiBranding
{
    private const string FaviconLinkTagId = "ngb-watchdog-dashboard-favicon";

    public static bool IsUiRequest(HttpRequest request, WatchdogOptions options)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        return request.Path.StartsWithSegments(options.UiPath, StringComparison.OrdinalIgnoreCase);
    }

    public static string InjectBranding(string html, string faviconHref)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var builder = new StringBuilder();
        builder.Append(NgbStandaloneTheme.HeadScriptTag);

        if (!string.IsNullOrWhiteSpace(faviconHref))
            builder.Append($"<link id=\"{FaviconLinkTagId}\" rel=\"icon\" type=\"image/svg+xml\" href=\"{faviconHref}\">");

        var markup = builder.ToString();
        var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        return headCloseIndex >= 0
            ? html.Insert(headCloseIndex, markup)
            : markup + html;
    }

    public static async Task InterceptHtmlAsync(
        HttpContext context,
        Func<Task> next,
        WatchdogOptions options,
        string faviconHref)
    {
        if (context is null)
            throw new NgbArgumentRequiredException(nameof(context));

        if (next is null)
            throw new NgbArgumentRequiredException(nameof(next));

        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (!IsUiRequest(context.Request, options))
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
            var injected = InjectBranding(html, faviconHref);
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
}
