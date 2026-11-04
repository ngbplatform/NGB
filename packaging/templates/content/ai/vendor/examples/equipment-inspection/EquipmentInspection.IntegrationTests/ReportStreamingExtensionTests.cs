using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Reporting.Streaming;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportStreamingExtensionTests
{
    [Fact]
    public async Task Named_export_executor_keeps_authorization_and_cleans_up_failure_before_headers()
    {
        var probe = new StreamProbe();
        await using var host = CreateHost(probe);
        await host.StartAsync();
        using var reader = await host.CreateClientAsync("reader");
        using var admin = await host.CreateClientAsync("administrator");
        var path = "/api/reports/" + InspectionReports.Summary + "/export/xlsx";
        using var denied = await reader.PostAsJsonAsync(path, Request());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, probe.Prepared);

        probe.FailBeforeFirstRow = true;
        using var failed = await admin.PostAsJsonAsync(path, Request());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(1, probe.Prepared);
        Assert.Equal(1, probe.Disposed);
        Assert.True(probe.SawReadSession);

        probe.FailBeforeFirstRow = false;
        using var accepted = await admin.PostAsJsonAsync(path, Request());
        await EnsureSuccessAsync(accepted);
        using var bytes = new MemoryStream(await accepted.Content.ReadAsByteArrayAsync());
        Assert.Contains("2025-12-01", Sheet(bytes));
        Assert.Equal(2, probe.Prepared);
        Assert.Equal(2, probe.Disposed);
    }

    [Fact]
    public async Task Prepared_export_freezes_input_and_disposal_releases_cancelled_read_sessions()
    {
        var probe = new StreamProbe();
        await using var host = CreateHost(probe);
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var downloads = services.GetRequiredService<IReportDownloadService>();
        var session = services.GetRequiredService<IReportReadSession>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var parameters = new Dictionary<string, string> { ["from_utc"] = "2025-12-01", ["to_utc"] = "2025-12-31" };
        var prepared = await downloads.PrepareAsync(InspectionReports.Summary,
            new ReportExportRequestDto(Parameters: parameters), CancellationToken.None);
        Assert.True(uow.HasActiveTransaction);
        parameters["from_utc"] = "2000-01-01";
        try
        {
            using var output = new MemoryStream();
            await prepared.WriteAsync(output, CancellationToken.None);
            var text = Sheet(output);
            Assert.Contains("2025-12-01", text);
            Assert.DoesNotContain("2000-01-01", text);
            await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.WriteAsync(output, CancellationToken.None));
        }
        finally
        {
            await prepared.DisposeAsync();
        }
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(Guid.Empty, session.SnapshotId);

        var cancelled = await downloads.PrepareAsync(InspectionReports.Summary, Request(), CancellationToken.None);
        try
        {
            using var output = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WriteAsync(output, cancellation.Token));
        }
        finally
        {
            await cancelled.DisposeAsync();
        }
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal(Guid.Empty, session.SnapshotId);
        Assert.Equal(2, probe.Disposed);
    }

    private static InspectionHost CreateHost(StreamProbe probe) => new(configureServices: services =>
        services.AddScoped<IStreamingReportExecutor>(provider => new InspectionExport(
            provider.GetRequiredService<IReportReadSession>(), probe)));

    private static ReportExportRequestDto Request() => new(Parameters: new Dictionary<string, string>
    {
        ["from_utc"] = "2025-12-01",
        ["to_utc"] = "2025-12-31"
    });

    private static string Sheet(MemoryStream bytes)
    {
        bytes.Position = 0;
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }

    private sealed class StreamProbe
    {
        public int Prepared { get; set; }
        public int Disposed { get; set; }
        public bool FailBeforeFirstRow { get; set; }
        public bool SawReadSession { get; set; }
    }

    // Synthetic rows isolate the public export extension contract from report calculations.
    private sealed class InspectionExport(IReportReadSession session, StreamProbe probe) : IStreamingReportExecutor
    {
        public string ReportCode => InspectionReports.Summary;

        public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
        {
            probe.Prepared++;
            return JsonSerializer.Serialize(request.Parameters!["from_utc"]);
        }

        public ReportSheetDto Template(string preparedJson)
            => new([new("period", "Frozen period", "string")], [], new(Title: "Extension contract example"));

        public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                probe.SawReadSession = session.SnapshotId != Guid.Empty;
                if (probe.FailBeforeFirstRow)
                {
                    throw new InvalidOperationException("Deliberate executor failure before the first row.");
                }
                var value = JsonSerializer.Deserialize<string>(preparedJson)!;
                for (var ordinal = 0; ordinal < 2; ordinal++)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new(ordinal, new(ReportRowKind.Detail,
                        [new(JsonSerializer.SerializeToElement(value), value, "string")]));
                    await Task.Yield();
                }
            }
            finally
            {
                probe.Disposed++;
            }
        }
    }
}
