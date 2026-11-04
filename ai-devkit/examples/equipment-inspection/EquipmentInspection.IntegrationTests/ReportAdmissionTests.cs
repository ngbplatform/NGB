using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Api.Reporting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportAdmissionTests
{
    [Fact]
    public async Task Page_and_download_capacity_are_independent_and_recover_after_busy_responses()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = new InspectionHost(configuration: new Dictionary<string, string>
        {
            ["Reporting:Requests:ConcurrentPages"] = "1",
            ["Reporting:Requests:QueuedPages"] = "0",
            ["Reporting:Requests:ConcurrentDownloads"] = "1",
            ["Reporting:Requests:QueuedDownloads"] = "0",
            ["Reporting:Requests:PageTimeoutSeconds"] = "17",
            ["Reporting:Requests:DownloadTimeoutSeconds"] = "23",
            ["Reporting:Cursor:SigningKey"] = key
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var budget = host.Services.GetRequiredService<ReportRequestBudget>();
        var admission = host.Services.GetRequiredService<IReportRequestAdmission>();
        Assert.Same(budget, admission);
        Assert.Equal(TimeSpan.FromSeconds(17), admission.ExecutionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(23), budget.Timeout(download: true));

        var request = Request();
        var path = "/api/reports/" + InspectionReports.Summary;
        var export = new ReportExportRequestDto(Parameters: request.Parameters);
        using (var page = await admission.AcquireAsync(CancellationToken.None))
        {
            Assert.NotNull(page);
            using var busy = await admin.PostAsJsonAsync(path + "/execute", request);
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(3), busy.Headers.RetryAfter!.Delta);
            using var download = await admin.PostAsJsonAsync(path + "/export/xlsx", export);
            await AssertWorkbookAsync(download);
        }

        using (var download = await budget.AcquireAsync(download: true, CancellationToken.None))
        {
            Assert.NotNull(download);
            Assert.True(download.IsAcquired);
            using var busy = await admin.PostAsJsonAsync(path + "/export/xlsx", export);
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(3), busy.Headers.RetryAfter!.Delta);
            var page = await PostAsync<ReportExecutionResponseDto>(admin, path + "/execute", request);
            Assert.False(page.HasMore);
        }

        await PostAsync<ReportExecutionResponseDto>(admin, path + "/execute", request);
        using var recovered = await admin.PostAsJsonAsync(path + "/export/xlsx", export);
        await AssertWorkbookAsync(recovered);
        Assert.Equal(1, budget.Statistics(download: false).CurrentAvailablePermits);
        Assert.Equal(1, budget.Statistics(download: true).CurrentAvailablePermits);
        Assert.Equal(0, budget.Statistics(download: false).CurrentQueuedCount);
        Assert.Equal(0, budget.Statistics(download: true).CurrentQueuedCount);

        using var replica = new ReportCursorProtector(Options.Create(new ReportCursorProtectionOptions { SigningKey = key }));
        using var other = new ReportCursorProtector(Options.Create(new ReportCursorProtectionOptions
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
        var cursor = host.Services.GetRequiredService<ReportCursorProtector>().Protect("replica continuation probe");
        Assert.Equal("replica continuation probe", replica.Unprotect(cursor));
        Assert.Throws<CryptographicException>(() => other.Unprotect(cursor));
    }

    [Fact]
    public async Task Cancelling_a_queued_report_releases_its_queue_slot_without_releasing_the_active_lease()
    {
        using var budget = new ReportRequestBudget(Options.Create(new ReportRequestLimits
        {
            ConcurrentPages = 1,
            QueuedPages = 1,
            PageQueueTimeoutSeconds = 30
        }));
        var admission = (IReportRequestAdmission)budget;
        using (var active = await admission.AcquireAsync(CancellationToken.None))
        {
            Assert.NotNull(active);
            using var cancelled = new CancellationTokenSource();
            var waiting = admission.AcquireAsync(cancelled.Token).AsTask();
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, budget.Statistics(download: false).CurrentQueuedCount);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(0, budget.Statistics(download: false).CurrentQueuedCount);
            Assert.Equal(0, budget.Statistics(download: false).CurrentAvailablePermits);
        }

        using var recovered = await admission.AcquireAsync(CancellationToken.None);
        Assert.NotNull(recovered);
    }

    private static ReportExecutionRequestDto Request()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
        {
            ["from_utc"] = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to_utc"] = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
    }

    private static async Task AssertWorkbookAsync(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        using var content = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
    }
}
