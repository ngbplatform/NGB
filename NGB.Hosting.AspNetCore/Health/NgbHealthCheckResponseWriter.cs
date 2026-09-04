using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NGB.Hosting.AspNetCore.Health;

/// <summary>
/// Writes the canonical NGB health-check response consumed by operability tooling.
/// </summary>
public static class NgbHealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(report);

        return UIResponseWriter.WriteHealthCheckUIResponse(httpContext, report);
    }
}
