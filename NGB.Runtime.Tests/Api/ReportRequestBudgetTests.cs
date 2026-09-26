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
        budget.Statistics(false).CurrentAvailablePermits.Should().Be(12);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Busy_downloads_are_rejected_before_execution_without_blocking_pages()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentDownloads = 1, QueuedDownloads = 0 }));
        using var active = await budget.AcquireAsync(true, CancellationToken.None);
        var context = Context();
        await new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context, () => throw new InvalidOperationException("Must not execute"));
        ((ObjectResult)context.Result!).StatusCode.Should().Be(429);
        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("3");
        budget.Statistics(false).CurrentAvailablePermits.Should().Be(12);
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
            budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
            cancellation.Cancel();
            context.HttpContext.RequestAborted.IsCancellationRequested.Should().BeTrue();
            throw new IOException("Disconnected");
        });
        await action.Should().ThrowAsync<IOException>();
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(1);
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
        budget.Statistics(false).CurrentAvailablePermits.Should().Be(12);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Five_simultaneous_downloads_complete_with_only_two_active_and_no_rejections()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits()));
        var releases = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var entered = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var contexts = Enumerable.Range(0, 5).Select(_ => Context()).ToArray();
        var tasks = Enumerable.Range(0, 5).Select(index =>
            new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(contexts[index], async () =>
            {
                entered[index].SetResult();
                await releases[index].Task;
                return new ResourceExecutedContext(contexts[index], []);
            })).ToArray();
        try
        {
            entered[0].Task.IsCompleted.Should().BeTrue();
            entered[1].Task.IsCompleted.Should().BeTrue();
            entered.Skip(2).Should().OnlyContain(signal => !signal.Task.IsCompleted);
            budget.Statistics(true).CurrentQueuedCount.Should().Be(3);
            budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
            for (var index = 0; index < 3; index++)
            {
                releases[index].SetResult();
                await entered[index + 2].Task.WaitAsync(TimeSpan.FromSeconds(5));
                budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
                budget.Statistics(true).CurrentQueuedCount.Should().Be(2 - index);
            }
        }
        finally
        {
            foreach (var release in releases) release.TrySetResult();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        contexts.Should().OnlyContain(context => context.Result == null);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(2);
        budget.Statistics(true).CurrentQueuedCount.Should().Be(0);
    }

    [Fact]
    public async Task Full_download_queue_rejects_excess_requests_and_preserves_fifo_order()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { ConcurrentDownloads = 1, QueuedDownloads = 2 }));
        using var active = await budget.AcquireAsync(true, CancellationToken.None);
        var first = budget.AcquireAsync(true, CancellationToken.None).AsTask();
        var second = budget.AcquireAsync(true, CancellationToken.None).AsTask();
        budget.Statistics(true).CurrentQueuedCount.Should().Be(2);
        var context = Context();
        await new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("An overflow request must not execute."));
        AssertBusy(context);
        using var page = await budget.AcquireAsync(false, CancellationToken.None);
        page.Should().NotBeNull("download pressure must not consume page permits");
        active!.Dispose();
        using var firstLease = await first.WaitAsync(TimeSpan.FromSeconds(5));
        firstLease.Should().NotBeNull();
        second.IsCompleted.Should().BeFalse();
        firstLease!.Dispose();
        using var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        secondLease.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_execute_and_immediately_frees_queue_capacity()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { ConcurrentDownloads = 1, QueuedDownloads = 1 }));
        using var active = await budget.AcquireAsync(true, CancellationToken.None);
        using var disconnected = new CancellationTokenSource();
        var context = Context();
        context.HttpContext.RequestAborted = disconnected.Token;
        var pending = new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("A cancelled waiter must not execute."));
        budget.Statistics(true).CurrentQueuedCount.Should().Be(1);
        disconnected.Cancel();
        var observe = async () => await pending;
        await observe.Should().ThrowAsync<OperationCanceledException>();
        context.Result.Should().BeNull("client cancellation must not be turned into HTTP 429");
        context.HttpContext.RequestAborted.Should().Be(disconnected.Token);
        budget.Statistics(true).CurrentQueuedCount.Should().Be(0);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
        var replacement = budget.AcquireAsync(true, CancellationToken.None).AsTask();
        replacement.IsCompleted.Should().BeFalse();
        active!.Dispose();
        using var lease = await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task Queue_deadline_returns_429_without_execution_or_stealing_an_active_slot()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { ConcurrentDownloads = 1, QueuedDownloads = 1, DownloadQueueTimeoutSeconds = 1 }));
        using var active = await budget.AcquireAsync(true, CancellationToken.None);
        var context = Context();
        var pending = new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("An expired waiter must not execute."));
        pending.IsCompleted.Should().BeFalse();
        budget.Statistics(true).CurrentQueuedCount.Should().Be(1);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        AssertBusy(context);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
        budget.Statistics(true).CurrentQueuedCount.Should().Be(0);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
        active!.Dispose();
        using var recovered = await budget.AcquireAsync(true, CancellationToken.None);
        recovered.Should().NotBeNull();
    }

    [Fact]
    public async Task Busy_pages_still_reject_immediately_without_using_download_capacity()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentPages = 1 }));
        using var page = await budget.AcquireAsync(false, CancellationToken.None);
        var context = Context();
        var rejected = new ReportRequestBudgetFilter(false, budget).OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("A busy page must not execute."));
        rejected.IsCompleted.Should().BeTrue();
        await rejected;
        AssertBusy(context);
        budget.Statistics(false).CurrentQueuedCount.Should().Be(0);
        using var download = await budget.AcquireAsync(true, CancellationToken.None);
        download.Should().NotBeNull();
    }

    [Fact]
    public async Task Already_cancelled_request_neither_executes_nor_consumes_capacity()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var context = Context();
        context.HttpContext.RequestAborted = cancelled.Token;
        var action = () => new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("A cancelled request must not execute."));
        await action.Should().ThrowAsync<OperationCanceledException>();
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(2);
        budget.Statistics(true).CurrentQueuedCount.Should().Be(0);
    }

    [Fact]
    public async Task Failed_execution_releases_its_slot_to_the_next_waiter()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentDownloads = 1 }));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstContext = Context();
        var first = new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(firstContext, async () =>
        {
            await release.Task;
            throw new IOException("Export failed.");
        });
        var secondContext = Context();
        var executed = false;
        var second = new ReportRequestBudgetFilter(true, budget).OnResourceExecutionAsync(secondContext, () =>
        {
            executed = true;
            return Task.FromResult(new ResourceExecutedContext(secondContext, []));
        });
        executed.Should().BeFalse();
        release.SetResult();
        var observe = async () => await first;
        await observe.Should().ThrowAsync<IOException>();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        executed.Should().BeTrue();
        firstContext.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(1);
    }

    private static void AssertBusy(ResourceExecutingContext context)
    {
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(429);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Status.Should().Be(429);
        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("3");
    }

    private static ResourceExecutingContext Context() => new(
        new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [], new List<IValueProviderFactory>());
}
