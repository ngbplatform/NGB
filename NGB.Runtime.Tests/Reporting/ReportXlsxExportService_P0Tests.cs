using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportXlsxExportService_P0Tests
{
    [Fact]
    public async Task ExportXlsxAsync_Smoke_Generates_OpenXml_Package()
    {
        var service = new ReportXlsxExportService();
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("account_display", "Account", "string", SemanticRole: "row-group"),
                new ReportSheetColumnDto("debit_amount__sum", "Debit", "decimal", SemanticRole: "measure")
            ],
            Rows:
            [
                new ReportSheetRowDto(ReportRowKind.Detail, [new ReportCellDto(Display: "1100 — AR", ValueType: "string"), new ReportCellDto(Value: Json(125m), Display: "125", ValueType: "decimal")])
            ],
            Meta: new ReportSheetMetaDto(Title: "Ledger Analysis"));

        var bytes = await service.ExportXlsxAsync(sheet, "Ledger Analysis", CancellationToken.None);

        bytes.Should().NotBeEmpty();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        archive.GetEntry("xl/styles.xml").Should().NotBeNull();
        archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();

        var workbookXml = ReadXml(archive, "xl/workbook.xml");
        workbookXml.ToString().Should().Contain("Ledger Analysis");
    }

    [Fact]
    public async Task ExportXlsxAsync_PivotHeaders_Create_Merged_Ranges()
    {
        var service = new ReportXlsxExportService();
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("account_display", "Account", "string", SemanticRole: "row-group"),
                new ReportSheetColumnDto("jan_debit", "Debit", "decimal", SemanticRole: "measure"),
                new ReportSheetColumnDto("feb_debit", "Debit", "decimal", SemanticRole: "measure"),
                new ReportSheetColumnDto("total_debit", "Total", "decimal", SemanticRole: "pivot-total")
            ],
            Rows:
            [
                new ReportSheetRowDto(ReportRowKind.Detail,
                [
                    new ReportCellDto(Display: "1100 — AR", ValueType: "string"),
                    new ReportCellDto(Value: Json(100m), Display: "100", ValueType: "decimal"),
                    new ReportCellDto(Value: Json(25m), Display: "25", ValueType: "decimal"),
                    new ReportCellDto(Value: Json(125m), Display: "125", ValueType: "decimal")
                ])
            ],
            Meta: new ReportSheetMetaDto(Title: "Ledger Analysis", IsPivot: true, HasColumnGroups: true),
            HeaderRows:
            [
                new ReportSheetRowDto(ReportRowKind.Header,
                [
                    new ReportCellDto(Display: "Account", ColSpan: 1, RowSpan: 2, StyleKey: "header"),
                    new ReportCellDto(Display: "2026", ColSpan: 2, RowSpan: 1, StyleKey: "header"),
                    new ReportCellDto(Display: "Total", ColSpan: 1, RowSpan: 2, StyleKey: "header")
                ]),
                new ReportSheetRowDto(ReportRowKind.Header,
                [
                    new ReportCellDto(Display: "Debit", StyleKey: "header"),
                    new ReportCellDto(Display: "Credit", StyleKey: "header")
                ])
            ]);

        var bytes = await service.ExportXlsxAsync(sheet, "Pivot", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var worksheetXml = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var mergeRefs = worksheetXml.Descendants().Where(x => x.Name.LocalName == "mergeCell").Select(x => x.Attribute("ref")?.Value).ToList();

        mergeRefs.Should().Contain(["A1:A2", "B1:C1", "D1:D2"]);
    }

    [Fact]
    public async Task ExportXlsxAsync_Subtotal_And_Total_Rows_Are_Present()
    {
        var service = new ReportXlsxExportService();
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("account_display", "Account", "string", SemanticRole: "row-group"),
                new ReportSheetColumnDto("debit_amount__sum", "Debit", "decimal", SemanticRole: "measure")
            ],
            Rows:
            [
                new ReportSheetRowDto(ReportRowKind.Group, [new ReportCellDto(Display: "1100 — AR", ValueType: "string"), new ReportCellDto(Display: "")]),
                new ReportSheetRowDto(ReportRowKind.Subtotal, [new ReportCellDto(Display: "1100 — AR subtotal", ValueType: "string"), new ReportCellDto(Value: Json(125m), Display: "125", ValueType: "decimal")]),
                new ReportSheetRowDto(ReportRowKind.Total, [new ReportCellDto(Display: "Total", ValueType: "string"), new ReportCellDto(Value: Json(125m), Display: "125", ValueType: "decimal")])
            ],
            Meta: new ReportSheetMetaDto(Title: "Totals"));

        var bytes = await service.ExportXlsxAsync(sheet, "Totals", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var worksheetXmlText = ReadEntryText(archive, "xl/worksheets/sheet1.xml");
        worksheetXmlText.Should().Contain("1100 — AR subtotal");
        worksheetXmlText.Should().Contain("Total");
        worksheetXmlText.Should().Contain(">125<");
    }

    [Fact]
    public async Task ExportXlsxAsync_Multiline_Hierarchy_Header_Is_Preserved()
    {
        var service = new ReportXlsxExportService();
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("__row_hierarchy", "Account\nPeriod\nDocument", "string", SemanticRole: "row-group"),
                new ReportSheetColumnDto("debit_amount__sum", "Debit", "decimal", SemanticRole: "measure")
            ],
            Rows:
            [
                new ReportSheetRowDto(ReportRowKind.Group,
                [
                    new ReportCellDto(Display: "1100 — AR", ValueType: "string"),
                    new ReportCellDto(Value: Json(125m), Display: "125", ValueType: "decimal")
                ])
            ],
            Meta: new ReportSheetMetaDto(Title: "Ledger Analysis"));

        var bytes = await service.ExportXlsxAsync(sheet, "Ledger Analysis", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var worksheetXml = ReadXml(archive, "xl/worksheets/sheet1.xml");
        worksheetXml.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value)
            .Should().Contain("Account\nPeriod\nDocument");
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(stream);
    }

    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static System.Text.Json.JsonElement Json<T>(T value) => System.Text.Json.JsonSerializer.SerializeToElement(value);
}
