using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NGB.BackgroundJobs.Hosting;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Hosting;

public sealed class BackgroundJobsDashboardBrandingFullCoverageTests
{
    [Fact]
    public void BuildInlineStyles_CoversGuardsEmptyRootedRelativeMissingAndEscaping()
    {
        Action nullOptions = () => BackgroundJobsDashboardBranding.BuildInlineStyles("root", null!);
        nullOptions.Should().Throw<NgbArgumentRequiredException>();
        Action blankRoot = () => BackgroundJobsDashboardBranding.BuildInlineStyles(" ", new BackgroundJobsHostingOptions());
        blankRoot.Should().Throw<NgbArgumentRequiredException>();

        var empty = new BackgroundJobsHostingOptions();
        empty.DashboardStylesheetPaths.Clear();
        BackgroundJobsDashboardBranding.BuildInlineStyles("root", empty).Should().BeEmpty();

        var root = Path.Combine(Path.GetTempPath(), $"ngb-background-brand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var rooted = Path.Combine(root, "rooted.css");
            var relative = Path.Combine(root, "relative.css");
            File.WriteAllText(rooted, "a{content:\"__NGB_DASHBOARD_BRAND_SUBTITLE__\";}");
            File.WriteAllText(relative, "b{color:red;}");
            var options = new BackgroundJobsHostingOptions { DashboardBrandSubtitle = "A\\B\"C" };
            options.DashboardStylesheetPaths.Clear();
            options.DashboardStylesheetPaths.Add(rooted);
            options.DashboardStylesheetPaths.Add("relative.css");

            var css = BackgroundJobsDashboardBranding.BuildInlineStyles(root, options);

            css.Should().Contain("A\\\\B\\\"C").And.Contain("b{color:red;}");
            options.DashboardStylesheetPaths.Clear();
            options.DashboardStylesheetPaths.Add(" ");
            Action blankPath = () => BackgroundJobsDashboardBranding.BuildInlineStyles(root, options);
            blankPath.Should().Throw<NgbArgumentRequiredException>();
            options.DashboardStylesheetPaths[0] = "missing.css";
            Action missingPath = () => BackgroundJobsDashboardBranding.BuildInlineStyles(root, options);
            missingPath.Should().Throw<NgbConfigurationViolationException>();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RequestAndInjectionHelpers_CoverEveryBranch()
    {
        var options = new BackgroundJobsHostingOptions();
        Action nullRequest = () => BackgroundJobsDashboardBranding.IsDashboardRequest(null!, options);
        nullRequest.Should().Throw<NgbArgumentRequiredException>();
        var context = new DefaultHttpContext();
        Action nullOptions = () => BackgroundJobsDashboardBranding.IsDashboardRequest(context.Request, null!);
        nullOptions.Should().Throw<NgbArgumentRequiredException>();

        context.Request.Method = HttpMethods.Post;
        BackgroundJobsDashboardBranding.IsDashboardRequest(context.Request, options).Should().BeFalse();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/other";
        BackgroundJobsDashboardBranding.IsDashboardRequest(context.Request, options).Should().BeFalse();
        context.Request.Method = HttpMethods.Head;
        context.Request.Path = "/hangfire/jobs";
        BackgroundJobsDashboardBranding.IsDashboardRequest(context.Request, options).Should().BeTrue();

        BackgroundJobsDashboardBranding.InjectBranding(" ", "css", "icon").Should().Be(" ");
        var withoutHead = BackgroundJobsDashboardBranding.InjectBranding("<body>x</body>", "", "");
        withoutHead.Should().StartWith("<script id=\"ngb-standalone-theme\"");
        withoutHead.Should().NotContain("favicon").And.NotContain("<style");
    }

    [Fact]
    public async Task InterceptHtmlAsync_CoversGuardsBypassNonHtmlHtmlAndFinallyRestoration()
    {
        var options = new BackgroundJobsHostingOptions();
        RequestDelegate noop = _ => Task.CompletedTask;
        Func<Task> nullContext = () => BackgroundJobsDashboardBranding.InterceptHtmlAsync(null!, noop, options, "css", "icon");
        await nullContext.Should().ThrowAsync<NgbArgumentRequiredException>();
        var guardContext = new DefaultHttpContext();
        Func<Task> nullNext = () => BackgroundJobsDashboardBranding.InterceptHtmlAsync(guardContext, null!, options, "css", "icon");
        await nullNext.Should().ThrowAsync<NgbArgumentRequiredException>();
        Func<Task> nullOptions = () => BackgroundJobsDashboardBranding.InterceptHtmlAsync(guardContext, noop, null!, "css", "icon");
        await nullOptions.Should().ThrowAsync<NgbArgumentRequiredException>();

        var bypassCalls = 0;
        guardContext.Request.Method = HttpMethods.Post;
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(
            guardContext, _ => { bypassCalls++; return Task.CompletedTask; }, options, "css", "icon");
        guardContext.Request.Method = HttpMethods.Get;
        guardContext.Request.Path = "/hangfire";
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(
            guardContext, _ => { bypassCalls++; return Task.CompletedTask; }, options, "", "");
        bypassCalls.Should().Be(2);

        var nonHtml = DashboardContext();
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(nonHtml, async _ =>
        {
            nonHtml.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await nonHtml.Response.WriteAsync("error");
        }, options, "css", "icon");
        (await ReadBody(nonHtml.Response)).Should().Be("error");

        var wrongType = DashboardContext();
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(wrongType, async _ =>
        {
            wrongType.Response.ContentType = "application/json";
            await wrongType.Response.WriteAsync("{}");
        }, options, "css", "icon");
        (await ReadBody(wrongType.Response)).Should().Be("{}");

        var missingType = DashboardContext();
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(missingType, async _ =>
        {
            await missingType.Response.WriteAsync("plain");
        }, options, "css", "icon");
        (await ReadBody(missingType.Response)).Should().Be("plain");

        var html = DashboardContext();
        var original = html.Response.Body;
        await BackgroundJobsDashboardBranding.InterceptHtmlAsync(html, async _ =>
        {
            html.Response.ContentType = "text/html; charset=utf-8";
            await html.Response.WriteAsync("<html><head></head><body>x</body></html>");
        }, options, "body{color:red}", "icon.svg");
        html.Response.Body.Should().BeSameAs(original);
        html.Response.ContentLength.Should().BeGreaterThan(0);
        (await ReadBody(html.Response)).Should().Contain("ngb-background-jobs-dashboard-theme");

        var throwing = DashboardContext();
        var throwingOriginal = throwing.Response.Body;
        var act = () => BackgroundJobsDashboardBranding.InterceptHtmlAsync(
            throwing, _ => throw new InvalidOperationException("boom"), options, "css", "icon");
        await act.Should().ThrowAsync<InvalidOperationException>();
        throwing.Response.Body.Should().BeSameAs(throwingOriginal);

        var middlewareCalls = 0;
        var middleware = new BackgroundJobsDashboardBrandingMiddleware(
            _ => { middlewareCalls++; return Task.CompletedTask; }, options, "", "");
        await middleware.InvokeAsync(guardContext);
        middlewareCalls.Should().Be(1);
    }

    private static DefaultHttpContext DashboardContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/hangfire";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBody(HttpResponse response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
