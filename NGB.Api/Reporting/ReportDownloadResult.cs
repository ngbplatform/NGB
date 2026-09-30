using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NGB.Application.Abstractions.Services;

namespace NGB.Api.Reporting;

/// <summary>Transport adapter; report execution and resource ownership stay behind the application contract.</summary>
internal sealed class ReportDownloadResult(IReportDownload download, string fileName) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        await using (download)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            response.Headers.CacheControl = "no-store";
            // Preserve response backpressure through nginx without spooling the workbook to its temp files.
            response.Headers["X-Accel-Buffering"] = "no";
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            response.Headers.ContentDisposition = disposition.ToString();

            try
            {
                await download.WriteAsync(response.Body, context.HttpContext.RequestAborted);
            }
            catch when (response.HasStarted)
            {
                // A partial ZIP must never be completed with a JSON error or treated as a successful download.
                context.HttpContext.Abort();
                throw;
            }
        }
    }
}
