using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace NGB.Api.Reporting;

public sealed class ReportRequestLimits
{
    public int ConcurrentPages { get; set; } = 12;
    public int QueuedPages { get; set; } = 48;
    public int PageQueueTimeoutSeconds { get; set; } = 1;
    public int ConcurrentDownloads { get; set; } = 2;
    public int QueuedDownloads { get; set; } = 4;
    public int DownloadQueueTimeoutSeconds { get; set; } = 3;
    public int PageTimeoutSeconds { get; set; } = 30;
    public int DownloadTimeoutSeconds { get; set; } = 300;
}

/// <summary>Per-instance limits with bounded FIFO queues before report preparation.</summary>
public sealed class ReportRequestBudget : IDisposable
{
    public const string MeterName = "NGB.Reporting";
    private readonly ReportRequestLimits _limits;
    private readonly ConcurrencyLimiter _pages;
    private readonly ConcurrencyLimiter _downloads;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly Counter<long> _admissions;
    private readonly Histogram<double> _waiting;
    private readonly object _statisticsGate = new();
    private bool _disposed;

    public ReportRequestBudget(IOptions<ReportRequestLimits> options, IMeterFactory? meters = null)
    {
        _limits = options.Value;
        _pages = CreateLimiter(_limits.ConcurrentPages, _limits.QueuedPages);
        _downloads = CreateLimiter(_limits.ConcurrentDownloads, _limits.QueuedDownloads);
        _ownsMeter = meters is null;
        _meter = meters?.Create(MeterName) ?? new Meter(MeterName);
        _admissions = _meter.CreateCounter<long>("ngb.report.admission.requests", "{request}");
        _waiting = _meter.CreateHistogram<double>("ngb.report.admission.duration", "s");
        _meter.CreateObservableGauge("ngb.report.admission.active", () => Observe(queued: false), "{request}");
        _meter.CreateObservableGauge("ngb.report.admission.queued", () => Observe(queued: true), "{request}");
    }

    /// <summary>Returns an owned execution lease, or null when admission is full or times out.</summary>
    public async ValueTask<RateLimitLease?> AcquireAsync(bool download, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "failed";

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waiting.CancelAfter(TimeSpan.FromSeconds(download
            ? _limits.DownloadQueueTimeoutSeconds
            : _limits.PageQueueTimeoutSeconds));

        try
        {
            var lease = await (download ? _downloads : _pages).AcquireAsync(1, waiting.Token);
            if (lease.IsAcquired)
            {
                outcome = "acquired";
                return lease;
            }

            outcome = "rejected";
            lease.Dispose();

            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
            // A queue deadline is capacity exhaustion; client cancellation propagates above.
            outcome = "timeout";
            return null;
        }
        finally
        {
            var tags = new TagList { { "report.kind", download ? "download" : "page" }, { "outcome", outcome } };
            _admissions.Add(1, tags);
            _waiting.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    public RateLimiterStatistics Statistics(bool download) => (download ? _downloads : _pages).GetStatistics()!;

    public TimeSpan Timeout(bool download)
        => TimeSpan.FromSeconds(download ? _limits.DownloadTimeoutSeconds : _limits.PageTimeoutSeconds);

    private IEnumerable<Measurement<long>> Observe(bool queued)
    {
        foreach (var download in new[] { false, true })
        {
            long value;
            lock (_statisticsGate)
            {
                var stats = _disposed ? null : Statistics(download);
                value = stats is null ? 0 : queued
                    ? stats.CurrentQueuedCount
                    : (download ? _limits.ConcurrentDownloads : _limits.ConcurrentPages) - stats.CurrentAvailablePermits;
            }

            yield return new Measurement<long>(value, new KeyValuePair<string, object?>("report.kind", download ? "download" : "page"));
        }
    }

    public void Dispose()
    {
        lock (_statisticsGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _pages.Dispose();
            _downloads.Dispose();
        }

        if (_ownsMeter)
            _meter.Dispose();
    }

    private static ConcurrencyLimiter CreateLimiter(int concurrent, int queued)
        => new(new ConcurrencyLimiterOptions
        {
            PermitLimit = concurrent,
            QueueLimit = queued,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
}

public sealed class ReportRequestBudgetAttribute : TypeFilterAttribute
{
    public ReportRequestBudgetAttribute(bool download = false)
        : base(typeof(ReportRequestBudgetFilter))
        => Arguments = [download];
}

public sealed class ReportRequestBudgetFilter(bool download, ReportRequestBudget budget) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var original = http.RequestAborted;
        using var admission = await budget.AcquireAsync(download, original);
        original.ThrowIfCancellationRequested();

        if (admission is null)
        {
            http.Response.Headers.RetryAfter = "3";
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 429,
                Title = "Report capacity is busy. Try again shortly."
            })
            {
                StatusCode = 429
            };

            return;
        }
        
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(original);
        deadline.CancelAfter(budget.Timeout(download));
        http.RequestAborted = deadline.Token;

        try
        {
            ResourceExecutedContext? result = null;
            try
            {
                result = await next();
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !original.IsCancellationRequested)
            {
                // Also handle a delegate that throws cancellation instead of returning it in the MVC result.
            }

            if (deadline.IsCancellationRequested && !original.IsCancellationRequested)
            {
                if (http.Response.HasStarted)
                {
                    http.Abort();
                }
                else
                {
                    if (result is not null)
                        result.ExceptionHandled = true;

                    http.Response.Clear();
                    http.Response.StatusCode = StatusCodes.Status504GatewayTimeout;

                    await http.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Status = 504, 
                            Title = "The report exceeded its request time limit. Narrow the filters and try again."
                        }, 
                        original);
                }
            }
        }
        finally
        {
            http.RequestAborted = original;
        }
    }
}
