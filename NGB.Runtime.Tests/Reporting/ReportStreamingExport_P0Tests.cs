using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportStreamingExport_P0Tests
{
    [Fact]
    public async Task Splits_sheets_repeats_headers_and_preserves_all_numeric_and_text_cells()
    {
        using var stream = new MemoryStream();
        var exporter = new ReportXlsxExportService();
        var template = new ReportSheetDto([new("label", "Label", "string"), new("amount", "Amount", "decimal")], [], new(Title: "Very long report title that exceeds thirty one characters"));
        await exporter.WriteXlsxAsync(stream, template, Rows(11), 5, default);
        stream.Position = 0;
        await using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        await using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
        var workbook = XDocument.Load(workbookStream);
        workbook.Descendants(ns + "sheet").Should().HaveCount(3);
        workbook.Descendants(ns + "sheet").Select(s => s.Attribute("name")!.Value).Should().OnlyHaveUniqueItems().And.OnlyContain(n => n.Length <= 31);
        var amounts = new List<decimal>();
        for (var i = 1; i <= 3; i++)
        {
            await using var sheetStream = archive.GetEntry($"xl/worksheets/sheet{i}.xml")!.Open();
            var sheet = XDocument.Load(sheetStream);
            var rows = sheet.Descendants(ns + "row").ToArray();
            rows.Length.Should().BeLessThanOrEqualTo(5);
            rows[0].Value.Should().Contain("Label");
            rows.Skip(1).SelectMany(r => r.Descendants(ns + "f")).Should().BeEmpty("text starting with '=' must not become an Excel formula");
            amounts.AddRange(rows.Skip(1).Select(r => decimal.Parse(r.Elements(ns + "c").ElementAt(1).Element(ns + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        amounts.Should().Equal(Enumerable.Range(0, 11).Select(i => i + 0.25m));
    }

    [Fact]
    public async Task Empty_stream_still_produces_a_valid_workbook_and_cancellation_stops_reading()
    {
        var exporter = new ReportXlsxExportService();
        var template = new ReportSheetDto([new("label", "Label", "string"), new("amount", "Amount", "decimal")], []);
        using var stream = new MemoryStream();
        await exporter.WriteXlsxAsync(stream, template, Rows(0), default);
        stream.Position = 0;
        await using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var cancelled = new MemoryStream();
        await ((Func<Task>)(() => exporter.WriteXlsxAsync(cancelled, template, Rows(5), cts.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    private static async IAsyncEnumerable<ReportSheetRowDto> Rows(int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        for (var i = 0; i < count; i++)
            yield return new(ReportRowKind.Detail, [new(Value: JsonSerializer.SerializeToElement("=1+1"), ValueType: "string"),
                new(Value: JsonSerializer.SerializeToElement(i + 0.25m), ValueType: "decimal")]);
    }
}
