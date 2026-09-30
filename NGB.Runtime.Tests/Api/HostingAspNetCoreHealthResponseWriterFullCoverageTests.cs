using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NGB.Hosting.AspNetCore.Health;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class HostingAspNetCoreHealthResponseWriterFullCoverageTests
{
    [Fact]
    public async Task WriteAsync_WritesCanonicalHealthChecksUiPayload()
    {
        var httpContext = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(),
            HealthStatus.Healthy,
            TimeSpan.Zero);

        await NgbHealthCheckResponseWriter.WriteAsync(httpContext, report);

        responseBody.Position = 0;
        using var payload = await JsonDocument.ParseAsync(responseBody);
        payload.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        payload.RootElement.GetProperty("entries").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_RejectsNullHttpContext()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(),
            HealthStatus.Healthy,
            TimeSpan.Zero);

        var act = () => NgbHealthCheckResponseWriter.WriteAsync(null!, report);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("httpContext");
    }

    [Fact]
    public async Task WriteAsync_RejectsNullHealthReport()
    {
        var act = () => NgbHealthCheckResponseWriter.WriteAsync(new DefaultHttpContext(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("report");
    }
}
