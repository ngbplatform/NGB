using System.IO.Compression;
using System.Xml;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Testing.Reporting;

internal static class ReportScaleAssertions
{
    public static async Task VerifyAsync(
        IReportEngine reports,
        IReportDownloadService downloads,
        string code,
        ReportExecutionRequestDto input,
        int minimumExportRows)
    {
        input = input with
        {
            DisablePaging = false,
            Cursor = null,
            Offset = 0
        };
       
        // Warm metadata separately so the comparison measures row-dependent queries.
        await reports.ExecuteAsync(code, input with { Limit = 1 }, default);
        using var small = new ReportPerformanceProbe(code, "scale-page-1");
        var one = await reports.ExecuteAsync(code, input with { Limit = 1 }, default);
        small.Rows = one.Sheet.Rows.Count;
        var smallCount = small.CommandCount;
        small.Dispose();
        using var larger = new ReportPerformanceProbe(code, "scale-page-200");
        var page = await reports.ExecuteAsync(code, input with { Limit = 200 }, default);
        larger.Rows = page.Sheet.Rows.Count;
        larger.CommandCount.Should().BeInRange(1, Math.Max(12, smallCount + 6), code + " must batch related-row lookups");
        page.Sheet.Rows.Should().NotBeEmpty(code);
        page.Sheet.Rows.Count.Should().BeLessThanOrEqualTo(1000);
        larger.Dispose();
        
        if (page.HasMore)
        {
            using var next = new ReportPerformanceProbe(code, "scale-continuation-200");
            var continued = await reports.ExecuteAsync(code, input with { Limit = 200, Cursor = page.NextCursor }, default);
            continued.Sheet.Rows.Should().NotBeEmpty(code);
            next.CommandCount.Should().BeInRange(1, Math.Max(16, smallCount + 10), code);
            next.Rows = continued.Sheet.Rows.Count;
        }
        
        var child = one.Sheet.Rows.FirstOrDefault(r => r.ChildrenPath is not null)?.ChildrenPath;
        if (child is not null)
        {
            using var childTrace = new ReportPerformanceProbe(code, "scale-child-200");
            var children = await reports.ExecuteAsync(code, input with { Limit = 200, GroupPath = child }, default);
            children.Sheet.Rows.Should().NotBeEmpty(code);
            childTrace.CommandCount.Should().BeInRange(1, 24, code);
            childTrace.Rows = children.Sheet.Rows.Count;
        }
        
        using var exported = new ReportPerformanceProbe(code, "scale-full-xlsx");
        await using var download = await downloads.PrepareAsync(
            code,
            new ReportExportRequestDto(Parameters: input.Parameters, Filters: input.Filters, Layout: input.Layout),
            default);

        var path = Path.Combine(Path.GetTempPath(), "ngb-report-scale-" + Guid.NewGuid().ToString("N") + ".xlsx");

        await using var file = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            65536,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        await download.WriteAsync(file, default);
        exported.Bytes = file.Length;
        file.Position = 0;
        await using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        await using var worksheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        using var xml = XmlReader.Create(worksheet, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var rows = 0;

        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "row") rows++;
        }

        rows.Should().BeGreaterThanOrEqualTo(minimumExportRows, code + " must export the complete large fixture, not only its first page");
        exported.Rows = rows;

        // Export reads in batches; expensive source declarations must not repeat per page/row.
        exported.SqlCommands.Count(sql => sql.TrimStart().StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase))
            .Should().BeLessThanOrEqualTo(16, code);
        exported.CommandCount.Should().BeLessThanOrEqualTo(64 + (rows / 500 + 1) * 8,
            code + " must batch export reads and enrichment rather than query once per row");
    }
}
