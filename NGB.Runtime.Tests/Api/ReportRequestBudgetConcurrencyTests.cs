using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Api.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed partial class ReportRequestBudgetTests
{
    [Theory]
    [InlineData(false, 12, 48)]
    [InlineData(true, 2, 4)]
    public async Task Full_default_burst_drains_in_fifo_order_without_losing_permits(bool download, int concurrent, int queued)
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits()));
        var owned = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            for (var i = 0; i < concurrent; i++) owned.Add((await budget.AcquireAsync(download, default))!);
            var pending = Enumerable.Range(0, queued).Select(_ => budget.AcquireAsync(download, default).AsTask()).ToArray();
            (await budget.AcquireAsync(download, default)).Should().BeNull();
            budget.Statistics(download).CurrentQueuedCount.Should().Be(queued);
            for (var i = 0; i < queued; i++)
            {
                owned[i].Dispose();
                var lease = await pending[i].WaitAsync(TimeSpan.FromSeconds(5));
                lease.Should().NotBeNull();
                owned.Add(lease!);
                pending.Skip(i + 1).Should().OnlyContain(task => !task.IsCompleted);
                budget.Statistics(download).CurrentAvailablePermits.Should().Be(0);
            }
        }
        finally { foreach (var lease in owned) lease.Dispose(); }
        budget.Statistics(download).CurrentAvailablePermits.Should().Be(concurrent);
        budget.Statistics(download).CurrentQueuedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_cancellation_release_races_do_not_leak_admission_or_queue_slots(bool download)
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { ConcurrentPages = 2, ConcurrentDownloads = 2, QueuedPages = 4, QueuedDownloads = 4 }));
        for (var wave = 0; wave < 64; wave++)
        {
            using var first = await budget.AcquireAsync(download, default);
            using var second = await budget.AcquireAsync(download, default);
            using var cancellation = new CancellationTokenSource();
            var pending = Enumerable.Range(0, 4).Select(async index =>
            {
                try
                {
                    using var lease = await budget.AcquireAsync(download, index % 2 == 0 ? cancellation.Token : default);
                    lease.Should().NotBeNull("a non-cancelled admitted waiter must eventually execute");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested && index % 2 == 0) { }
            }).ToArray();
            budget.Statistics(download).CurrentQueuedCount.Should().Be(4);
            await Task.WhenAll(Task.Run(cancellation.Cancel), Task.Run(() => { first!.Dispose(); second!.Dispose(); }));
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
            budget.Statistics(download).CurrentAvailablePermits.Should().Be(2);
            budget.Statistics(download).CurrentQueuedCount.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_execution_cancellation_from_own_deadline_returns_504(bool download)
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { PageTimeoutSeconds = 0, DownloadTimeoutSeconds = 0 }));
        var context = Context();
        context.HttpContext.Response.Body = new MemoryStream();
        await new ReportRequestBudgetFilter(download, budget).OnResourceExecutionAsync(context, async () =>
        {
            await Task.Delay(Timeout.Infinite, context.HttpContext.RequestAborted);
            return new ResourceExecutedContext(context, []);
        });
        context.HttpContext.Response.StatusCode.Should().Be(504);
        budget.Statistics(download).CurrentAvailablePermits.Should().Be(download ? 2 : 12);
        context.HttpContext.RequestAborted.Should().Be(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_cancellation_during_execution_is_not_replaced_by_gateway_timeout(bool download)
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits()));
        using var cancellation = new CancellationTokenSource();
        var context = Context();
        context.HttpContext.RequestAborted = cancellation.Token;
        Func<Task> run = () => new ReportRequestBudgetFilter(download, budget).OnResourceExecutionAsync(context, () =>
        {
            cancellation.Cancel();
            context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable");
        });
        await run.Should().ThrowAsync<OperationCanceledException>();
        context.HttpContext.Response.StatusCode.Should().Be(200);
        context.Result.Should().BeNull();
        context.HttpContext.RequestAborted.Should().Be(cancellation.Token);
        budget.Statistics(download).CurrentAvailablePermits.Should().Be(download ? 2 : 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_completes_waiters_and_does_not_leave_background_tasks(bool download)
    {
        var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits { ConcurrentPages = 1, ConcurrentDownloads = 1 }));
        using var active = await budget.AcquireAsync(download, default);
        var pending = budget.AcquireAsync(download, default).AsTask();
        pending.IsCompleted.Should().BeFalse();
        budget.Dispose();
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
        budget.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_metrics_distinguish_full_queue_deadline_and_client_cancellation(bool download)
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var meter = factory.Create(ReportRequestBudget.MeterName);
        var counts = new ConcurrentQueue<(string Instrument, long Value, string Kind, string? Outcome)>();
        var durations = new ConcurrentQueue<double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            { if (ReferenceEquals(instrument.Meter, meter)) current.EnableMeasurementEvents(instrument); }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
            counts.Enqueue((instrument.Name, value, values["report.kind"]!, values.GetValueOrDefault("outcome")));
        });
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Enqueue(value));
        listener.Start();
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        { ConcurrentPages = 1, ConcurrentDownloads = 1, QueuedPages = 1, QueuedDownloads = 1,
          PageQueueTimeoutSeconds = 1, DownloadQueueTimeoutSeconds = 1 }), factory);
        using var active = await budget.AcquireAsync(download, default);
        var timeout = budget.AcquireAsync(download, default).AsTask();
        (await budget.AcquireAsync(download, default)).Should().BeNull();
        listener.RecordObservableInstruments();
        (await timeout.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
        // The limiter may signal the expired waiter's task just before updating its statistics.
        using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (budget.Statistics(download).CurrentQueuedCount != 0)
            await Task.Delay(1, cleanupDeadline.Token);
        using var cancellation = new CancellationTokenSource();
        var cancelled = budget.AcquireAsync(download, cancellation.Token).AsTask();
        cancellation.Cancel();
        await ((Func<Task>)(async () => await cancelled)).Should().ThrowAsync<OperationCanceledException>();
        var kind = download ? "download" : "page";
        counts.Where(row => row.Instrument == "ngb.report.admission.requests").Select(row => (row.Value, row.Kind, row.Outcome))
            .Should().BeEquivalentTo(new[] { (1L, kind, "acquired"), (1L, kind, "rejected"), (1L, kind, "timeout"), (1L, kind, "cancelled") });
        counts.Should().Contain(("ngb.report.admission.active", 1L, kind, null));
        counts.Should().Contain(("ngb.report.admission.queued", 1L, kind, null));
        durations.Should().HaveCount(4).And.OnlyContain(value => value >= 0);
        budget.Dispose();
        listener.RecordObservableInstruments();
    }
}
