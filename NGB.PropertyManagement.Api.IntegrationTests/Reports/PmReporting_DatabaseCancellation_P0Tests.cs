using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Api.Reporting;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_DatabaseCancellation_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("page", false)] [InlineData("json", false)] [InlineData("form", false)]
    [InlineData("page", true)] [InlineData("json", true)] [InlineData("form", true)]
    public async Task Http_cancel_or_deadline_stops_real_SQL_and_returns_connection_to_single_slot_pool(string kind, bool timeout)
    {
        var applicationName = "report-cancellation-" + Guid.NewGuid().ToString("N");
        var appConnection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = applicationName, MaxPoolSize = 1 };
        await using var factory = new PmApiFactory(fixture, appConnection.ConnectionString, new Dictionary<string, string?>
        {
            ["Reporting:Requests:ConcurrentPages"] = "1",
            ["Reporting:Requests:ConcurrentDownloads"] = "1",
            ["Reporting:Requests:PageTimeoutSeconds"] = timeout ? "3" : "30",
            ["Reporting:Requests:DownloadTimeoutSeconds"] = timeout ? "3" : "30"
        });
        using var client = factory.CreateClient();
        var budget = factory.Services.GetRequiredService<ReportRequestBudget>();
        // Warm authentication, report metadata and the real data path before measuring cancellation.
        using (var warm = await SendAsync(client, kind)) await AssertRealReportAsync(warm, kind);
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await blocker.ExecuteAsync("LOCK TABLE accounting_register_main IN ACCESS EXCLUSIVE MODE", transaction: transaction);
        var blockerPid = blocker.ProcessID;
        await using var observer = new NpgsqlConnection(fixture.ConnectionString);
        await observer.OpenAsync();
        using var cancel = new CancellationTokenSource();
        var pending = SendAsync(client, kind, cancel.Token);
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await observer.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                  WHERE application_name = @Name AND @Blocker = ANY(pg_blocking_pids(pid)))
                """, new { Name = applicationName, Blocker = blockerPid }, cancellationToken: wait.Token)))
            {
                pending.IsCompleted.Should().BeFalse("the actual report SQL must reach the held table lock");
                await Task.Delay(5, wait.Token);
            }
            if (timeout)
            {
                using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
            }
            else
            {
                cancel.Cancel();
                await ((Func<Task>)(async () => { using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10)); }))
                    .Should().ThrowAsync<OperationCanceledException>();
            }
            // The lock is STILL held. Disappearance of server work is cancellation, not query completion.
            while (await observer.ExecuteScalarAsync<bool>(new CommandDefinition("""
                       SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = @Name
                         AND (state = 'active' OR state LIKE 'idle in transaction%'))
                       """, new { Name = applicationName }, cancellationToken: wait.Token))
                   || budget.Statistics(kind != "page").CurrentAvailablePermits != 1)
                await Task.Delay(5, wait.Token);
            budget.Statistics(kind != "page").CurrentQueuedCount.Should().Be(0);
        }
        finally
        {
            cancel.Cancel();
            await transaction.RollbackAsync();
            try { (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Dispose(); }
            catch (OperationCanceledException) { }
        }
        // MaxPoolSize=1 makes a leaked connection observable; this is a real report/XLSX export.
        using var recovered = await SendAsync(client, kind).WaitAsync(TimeSpan.FromSeconds(10));
        await AssertRealReportAsync(recovered, kind);
        budget.Statistics(kind != "page").CurrentAvailablePermits.Should().Be(1);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string kind, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string> { ["from_utc"] = "2026-03-01", ["to_utc"] = "2026-03-31" };
        using HttpContent content = kind switch
        {
            "page" => JsonContent.Create(new ReportExecutionRequestDto(Parameters: parameters)),
            "form" => new FormUrlEncodedContent(new Dictionary<string, string>
                { ["request"] = JsonSerializer.Serialize(new ReportExportRequestDto(Parameters: parameters)) }),
            _ => JsonContent.Create(new ReportExportRequestDto(Parameters: parameters))
        };
        return await client.PostAsync(ReportAdmissionHarness.Route(kind), content, ct);
    }

    private static async Task AssertRealReportAsync(HttpResponseMessage response, string kind)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (kind == "page")
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        else
        {
            using var archive = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        }
    }
}
