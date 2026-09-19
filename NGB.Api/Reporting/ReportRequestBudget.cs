using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace NGB.Api.Reporting;

public sealed class ReportRequestLimits
{
    public int ConcurrentPages { get; set; } = 12;
    public int ConcurrentDownloads { get; set; } = 2;
    public int PageTimeoutSeconds { get; set; } = 30;
    public int DownloadTimeoutSeconds { get; set; } = 300;
}

/// <summary>Per API instance admission limits; no unbounded queue holds HTTP requests or database connections.</summary>
public sealed class ReportRequestBudget(IOptions<ReportRequestLimits> options) : IDisposable
{
    private readonly SemaphoreSlim _pages = new(options.Value.ConcurrentPages, options.Value.ConcurrentPages);
    private readonly SemaphoreSlim _downloads = new(options.Value.ConcurrentDownloads, options.Value.ConcurrentDownloads);

    public SemaphoreSlim Gate(bool download) => download ? _downloads : _pages;

    public TimeSpan Timeout(bool download) => TimeSpan.FromSeconds(download
        ? options.Value.DownloadTimeoutSeconds
        : options.Value.PageTimeoutSeconds);

    public void Dispose()
    {
        _pages.Dispose();
        _downloads.Dispose();
    }
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
        var gate = budget.Gate(download);

        if (!await gate.WaitAsync(0, original))
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
            gate.Release();
        }
    }
}
