using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace NGB.Api.Reporting;

public sealed class ReportRequestLimits
{
    public int ConcurrentPages { get; set; } = 12;
    public int ConcurrentDownloads { get; set; } = 2;
    public int QueuedDownloads { get; set; } = 4;
    public int DownloadQueueTimeoutSeconds { get; set; } = 3;
    public int PageTimeoutSeconds { get; set; } = 30;
    public int DownloadTimeoutSeconds { get; set; } = 300;
}

/// <summary>Per-instance limits with a bounded FIFO download queue before report preparation.</summary>
public sealed class ReportRequestBudget(IOptions<ReportRequestLimits> options) : IDisposable
{
    private readonly ConcurrencyLimiter _pages = CreateLimiter(options.Value.ConcurrentPages, 0);
    private readonly ConcurrencyLimiter _downloads = CreateLimiter(options.Value.ConcurrentDownloads, options.Value.QueuedDownloads);

    /// <summary>Returns an owned execution lease, or null when admission is full or times out.</summary>
    public async ValueTask<RateLimitLease?> AcquireAsync(bool download, CancellationToken ct)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (download)
            waiting.CancelAfter(TimeSpan.FromSeconds(options.Value.DownloadQueueTimeoutSeconds));

        try
        {
            var lease = await (download ? _downloads : _pages).AcquireAsync(1, waiting.Token);
            if (lease.IsAcquired)
                return lease;

            lease.Dispose();
            return null;
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // A queue deadline is capacity exhaustion; a client disconnect must still propagate.
            return null;
        }
    }

    public RateLimiterStatistics Statistics(bool download) => (download ? _downloads : _pages).GetStatistics()!;

    public TimeSpan Timeout(bool download) => TimeSpan.FromSeconds(download
        ? options.Value.DownloadTimeoutSeconds
        : options.Value.PageTimeoutSeconds);

    public void Dispose()
    {
        _pages.Dispose();
        _downloads.Dispose();
    }

    private static ConcurrencyLimiter CreateLimiter(int concurrent, int queued) => new(new ConcurrencyLimiterOptions
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
            var result = await next();

            if (deadline.IsCancellationRequested && !original.IsCancellationRequested)
            {
                if (http.Response.HasStarted)
                {
                    http.Abort();
                }
                else
                {
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
