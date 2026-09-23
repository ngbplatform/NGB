using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NGB.Api.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class ReportRequestBudgetTests
{
    [Fact]
    public async Task Deadline_after_headers_aborts_connection_and_releases_admission()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { PageTimeoutSeconds = 0 }));
        var context = Context();
        var response = new Moq.Mock<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>();
        response.SetupGet(x => x.HasStarted).Returns(true);
        context.HttpContext.Features.Set(response.Object);
        var lifetime = new Moq.Mock<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>();
        lifetime.SetupProperty(x => x.RequestAborted);
        context.HttpContext.Features.Set(lifetime.Object);
        await new ReportRequestBudgetFilter(false, budget).OnResourceExecutionAsync(context, async () =>
        {
            try { await Task.Delay(Timeout.Infinite, context.HttpContext.RequestAborted); }
            catch (OperationCanceledException) { }
            return new ResourceExecutedContext(context, []);
        });
        lifetime.Verify(x => x.Abort(), Moq.Times.Once);
        budget.Gate(false).CurrentCount.Should().Be(12);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Busy_downloads_are_rejected_before_execution_without_blocking_pages()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentDownloads = 1 }));
        await budget.Gate(true).WaitAsync();
        var context = Context();
        await new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Must not execute"));
        ((ObjectResult)context.Result!).StatusCode.Should().Be(429);
        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("3");
        budget.Gate(false).CurrentCount.Should().Be(12);
        budget.Gate(true).Release();
    }

    [Fact]
    public async Task Cancellation_and_failed_result_release_admission_and_restore_request_token()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentDownloads = 1 }));
        using var cancellation = new CancellationTokenSource();
        var context = Context();
        context.HttpContext.RequestAborted = cancellation.Token;
        var action = () => new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context, () =>
        {
            context.HttpContext.RequestAborted.Should().NotBe(cancellation.Token);
            budget.Gate(true).CurrentCount.Should().Be(0);
            cancellation.Cancel();
            context.HttpContext.RequestAborted.IsCancellationRequested.Should().BeTrue();
            throw new IOException("Disconnected");
        });
        await action.Should().ThrowAsync<IOException>();
        budget.Gate(true).CurrentCount.Should().Be(1);
        context.HttpContext.RequestAborted.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task Deadline_cancels_execution_returns_timeout_and_releases_admission()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { PageTimeoutSeconds = 1 }));
        var context = Context();
        context.HttpContext.Response.Body = new MemoryStream();
        ResourceExecutedContext? executed = null;
        await new ReportRequestBudgetFilter(false, budget).OnResourceExecutionAsync(context, async () =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.HttpContext.RequestAborted); }
            catch (OperationCanceledException exception)
            {
                executed = new ResourceExecutedContext(context, []) { Exception = exception };
            }
            return executed!;
        });
        context.HttpContext.Response.StatusCode.Should().Be(504);
        executed!.ExceptionHandled.Should().BeTrue();
        budget.Gate(false).CurrentCount.Should().Be(12);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
    }

    private static ResourceExecutingContext Context() => new(
        new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [], new List<IValueProviderFactory>());
}
