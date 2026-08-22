using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportXlsxExportServiceFullCoverageTests
{
    [Fact]
    public void ExportXlsxAsync_RejectsNullSheetMissingColumnsAndCancellation()
    {
        var service = new ReportXlsxExportService();
        ((Action)(() => service.ExportXlsxAsync(null!, null, CancellationToken.None)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("sheet");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ((Action)(() => service.ExportXlsxAsync(EmptySheet(), null, cancellation.Token)))
            .Should().Throw<OperationCanceledException>();

        var missingColumns = new ReportSheetDto(null!, []);
        ((Action)(() => service.ExportXlsxAsync(missingColumns, null, CancellationToken.None)))
            .Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*requires sheet columns*");
    }

    [Fact]
    public async Task ExportXlsxAsync_ExportsEveryCellKindValueConversionAndStyle()
    {
        var columns = Enumerable.Range(1, 45)
            .Select(index => new ReportSheetColumnDto(
                $"c{index}",
                $"Column {index}",
                "string",
                IsFrozen: index <= 2,
                SemanticRole: index <= 3 ? "row-group" : "measure"))
            .ToList();
        var headers = new List<ReportSheetRowDto>
        {
            Row(ReportRowKind.Header, new ReportCellDto(Display: "Merged", ColSpan: 45, RowSpan: 2)),
            Row(ReportRowKind.Header, new ReportCellDto(Display: "Blocked by row span")),
            Row(
                ReportRowKind.Header,
                Cell("date", "2026-01-02"),
                Cell("datetime", "2026-01-02T03:04:05"),
                Cell("int", "7"),
                Cell("decimal", "7.5"),
                Cell("string", "Header"))
        };
        var rows = new List<ReportSheetRowDto>
        {
            Row(ReportRowKind.Group, Cell("int", "7.9"), Cell("decimal", "7.5"), Cell("string", "Group")),
            Row(ReportRowKind.Subtotal, Cell("int64", "8"), Cell("double", "8.5"), Cell("string", "Subtotal")),
            Row(ReportRowKind.Total, Cell("long", "9"), Cell("float", "9.5"), Cell("string", "Total")),
            Row(ReportRowKind.Detail,
                Cell("decimal", JsonSerializer.SerializeToElement(1.25m)),
                Cell("double", JsonSerializer.SerializeToElement(2.25m)),
                Cell("float", JsonSerializer.SerializeToElement("3.25")),
                Cell("single", "4.25"),
                Cell("int", JsonSerializer.SerializeToElement(5.9m)),
                Cell("int32", "6.9"),
                Cell("int64", "7.9"),
                Cell("long", "8.9"),
                Cell("short", "9.9"),
                Cell("byte", "10.9"),
                Cell("time", "12:30:00"),
                Cell("date", "2026-03-04"),
                Cell("datetimeoffset", "2026-03-04T12:30:00+02:00"),
                Cell("datetime", "2026-03-04T12:30:00"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement<string?>(null), ValueType: "date"),
                new ReportCellDto(Display: "not-a-date", ValueType: "date"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(true), ValueType: "date"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(false), ValueType: "date"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(true), ValueType: "boolean"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(false), ValueType: "boolean"),
                new ReportCellDto(Display: "True", ValueType: "boolean"),
                new ReportCellDto(),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement("Text"), ValueType: "string"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(42), ValueType: "string"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(new { key = "value" }), ValueType: "string"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement("bad"), Display: "12.5", ValueType: "decimal"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement("bad"), ValueType: "decimal"),
                new ReportCellDto(Value: JsonSerializer.SerializeToElement(1e100), ValueType: "decimal"),
                new ReportCellDto(Display: "Normalized", ValueType: " STRING ")),
            Row((ReportRowKind)999, Cell(null, "Default style"))
        };
        var sheet = new ReportSheetDto(
            columns,
            rows,
            new ReportSheetMetaDto(Title: "Core title"),
            headers);
        var service = new ReportXlsxExportService(new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 3, 4, 5, TimeSpan.Zero)));

        var bytes = await service.ExportXlsxAsync(
            sheet,
            "  Bad\\/?*[]:Title That Is Deliberately Longer Than Thirty One Characters  ",
            CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var title = workbook.Descendants().Single(x => x.Name.LocalName == "sheet").Attribute("name")!.Value;
        title.Should().HaveLength(31);
        title.Should().NotContainAny("\\", "/", "?", "*", "[", "]", ":");

        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        worksheet.Descendants().Single(x => x.Name.LocalName == "pane").Attributes()
            .Select(x => x.Name.LocalName).Should().Contain(["xSplit", "ySplit"]);
        worksheet.Descendants().Single(x => x.Name.LocalName == "mergeCell").Attribute("ref")!.Value
            .Should().Be("A1:AS2");
        worksheet.Descendants().Where(x => x.Name.LocalName == "c").Select(x => x.Attribute("t")?.Value)
            .Should().Contain(["b", "inlineStr"]);
        worksheet.Descendants().Where(x => x.Name.LocalName == "c").Select(x => x.Attribute("s")?.Value)
            .Should().Contain(["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19"]);

        var core = ReadXml(archive, "docProps/core.xml");
        core.ToString(SaveOptions.DisableFormatting).Should().Contain("2026-08-22T03:04:05Z");
    }

    [Fact]
    public async Task ExportXlsxAsync_UsesFallbackTitleFlatEmptyHeaderAndOnlyVerticalFreeze()
    {
        var service = new ReportXlsxExportService();

        var bytes = await service.ExportXlsxAsync(EmptySheet(), " ", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = ReadXml(archive, "xl/workbook.xml");
        workbook.Descendants().Single(x => x.Name.LocalName == "sheet").Attribute("name")!.Value
            .Should().Be("Report");
        var worksheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var pane = worksheet.Descendants().Single(x => x.Name.LocalName == "pane");
        pane.Attribute("xSplit").Should().BeNull();
        pane.Attribute("ySplit")!.Value.Should().Be("1");
        pane.Attribute("activePane")!.Value.Should().Be("bottomLeft");
        worksheet.Descendants().Where(x => x.Name.LocalName == "col").Should().ContainSingle();

        var metaTitleBytes = await service.ExportXlsxAsync(
            new ReportSheetDto([], [], new ReportSheetMetaDto(Title: "Meta title")),
            null,
            CancellationToken.None);
        using var metaTitleArchive = new ZipArchive(new MemoryStream(metaTitleBytes), ZipArchiveMode.Read);
        ReadXml(metaTitleArchive, "xl/workbook.xml").Descendants()
            .Single(x => x.Name.LocalName == "sheet").Attribute("name")!.Value.Should().Be("Meta title");
    }

    private static ReportSheetDto EmptySheet() => new([], []);

    private static ReportSheetRowDto Row(ReportRowKind kind, params ReportCellDto[] cells)
        => new(kind, cells);

    private static ReportCellDto Cell(string? type, string display)
        => new(Display: display, ValueType: type);

    private static ReportCellDto Cell(string type, JsonElement value)
        => new(Value: value, ValueType: type);

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(stream);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
