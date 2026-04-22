using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

public sealed class ReportXlsxExportService(TimeProvider? timeProvider = null) : IReportExportService
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private static readonly XNamespace NsSpreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace NsPackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace NsContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace NsDc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace NsDcterms = "http://purl.org/dc/terms/";
    private static readonly XNamespace NsDcmiType = "http://purl.org/dc/dcmitype/";
    private static readonly XNamespace NsXsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace NsExtended = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<byte[]> ExportXlsxAsync(ReportSheetDto sheet, string? worksheetTitle, CancellationToken ct)
    {
        if (sheet is null)
            throw new NgbArgumentRequiredException(nameof(sheet));

        ct.ThrowIfCancellationRequested();

        var safeWorksheetTitle = SanitizeWorksheetTitle(worksheetTitle ?? sheet.Meta?.Title);
        var export = BuildWorksheetExport(sheet);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            WriteEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            WriteEntry(archive, "docProps/app.xml", BuildAppXml(safeWorksheetTitle));
            WriteEntry(archive, "docProps/core.xml", BuildCoreXml(sheet.Meta?.Title ?? safeWorksheetTitle));
            WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml(safeWorksheetTitle));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            WriteEntry(archive, "xl/styles.xml", BuildStylesXml());
            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(export));
        }

        return Task.FromResult(stream.ToArray());
    }

    private static WorksheetExport BuildWorksheetExport(ReportSheetDto sheet)
    {
        var columns = sheet.Columns 
                      ?? throw new NgbInvariantViolationException("Report xlsx export requires sheet columns.");
        var dataRows = sheet.Rows;
        var headerRows = sheet.HeaderRows?.Count > 0
            ? sheet.HeaderRows
            : [BuildFlatHeaderRow(columns)];

        var exportRows = new List<WorksheetRow>(headerRows.Count + dataRows.Count);
        var merges = new List<string>();
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var rowIndex = 1;

        foreach (var row in headerRows)
        {
            exportRows.Add(BuildWorksheetRow(row, rowIndex++, columns.Count, merges, occupied));
        }

        foreach (var row in dataRows)
        {
            exportRows.Add(BuildWorksheetRow(row, rowIndex++, columns.Count, merges, occupied));
        }

        var frozenColumns = CountFrozenColumns(columns);
        return new WorksheetExport(exportRows, merges, columns.Count, headerRows.Count, frozenColumns);
    }

    private static WorksheetRow BuildWorksheetRow(
        ReportSheetRowDto row,
        int rowIndex,
        int totalColumns,
        ICollection<string> merges,
        ISet<string> occupied)
    {
        var cells = new List<WorksheetCell>();
        var columnIndex = 1;

        foreach (var cell in row.Cells)
        {
            while (occupied.Contains(GetOccupiedKey(rowIndex, columnIndex)))
            {
                columnIndex++;
            }

            if (columnIndex > totalColumns)
                break;

            var colSpan = Math.Max(1, cell.ColSpan);
            var rowSpan = Math.Max(1, cell.RowSpan);
            var styleId = ResolveStyleId(row.RowKind, cell);
            cells.Add(new WorksheetCell(columnIndex, ConvertValue(cell), styleId));

            if (colSpan > 1 || rowSpan > 1)
            {
                var endColumn = columnIndex + colSpan - 1;
                var endRow = rowIndex + rowSpan - 1;
                merges.Add($"{GetCellReference(columnIndex, rowIndex)}:{GetCellReference(endColumn, endRow)}");

                for (var r = rowIndex; r <= endRow; r++)
                {
                    for (var c = columnIndex; c <= endColumn; c++)
                    {
                        if (r == rowIndex && c == columnIndex)
                            continue;

                        occupied.Add(GetOccupiedKey(r, c));
                    }
                }
            }

            columnIndex += colSpan;
        }

        return new WorksheetRow(rowIndex, cells);
    }

    private static ReportSheetRowDto BuildFlatHeaderRow(IReadOnlyList<ReportSheetColumnDto> columns)
        => new(
            RowKind: ReportRowKind.Header,
            Cells: columns
                .Select(col => new ReportCellDto(
                    Display: col.Title,
                    ValueType: "string",
                    StyleKey: "header",
                    SemanticRole: "header"))
                .ToList(),
            SemanticRole: "header");

    private static int CountFrozenColumns(IReadOnlyList<ReportSheetColumnDto> columns)
    {
        var explicitFrozen = columns.Count(x => x.IsFrozen);
        if (explicitFrozen > 0)
            return explicitFrozen;

        var implicitFrozen = 0;
        foreach (var column in columns)
        {
            if (!string.Equals(column.SemanticRole, "row-group", StringComparison.OrdinalIgnoreCase))
                break;

            implicitFrozen++;
        }

        return implicitFrozen;
    }

    private static CellExportValue ConvertValue(ReportCellDto cell)
    {
        if (cell.Value is null && string.IsNullOrWhiteSpace(cell.Display))
            return CellExportValue.Blank();

        var valueType = NormalizeValueType(cell.ValueType);
        if (IsDateValueType(valueType) && TryConvertDateLike(cell, valueType, out var dateValue))
            return dateValue;

        if (IsNumericValueType(valueType) && TryConvertNumeric(cell, valueType, out var numericValue))
            return numericValue;

        if (TryConvertBoolean(cell, out var boolValue))
            return boolValue;

        return CellExportValue.InlineString(cell.Display ?? ExtractString(cell.Value) ?? string.Empty);
    }

    private static bool TryConvertNumeric(ReportCellDto cell, string valueType, out CellExportValue value)
    {
        if (TryGetDecimal(cell, out var decimalValue))
        {
            value = IsIntegralValueType(valueType)
                ? CellExportValue.Number(decimal.Truncate(decimalValue).ToString(CultureInfo.InvariantCulture))
                : CellExportValue.Number(decimalValue.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        value = CellExportValue.Blank();
        return false;
    }

    private static bool TryConvertDateLike(ReportCellDto cell, string valueType, out CellExportValue value)
    {
        var raw = ExtractString(cell.Value) ?? cell.Display;
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = CellExportValue.Blank();
            return false;
        }

        if (valueType == "time" && TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
        {
            var serial = timeOnly.ToTimeSpan().TotalDays.ToString(CultureInfo.InvariantCulture);
            value = CellExportValue.Number(serial);
            return true;
        }

        if (valueType == "date" && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            var serial = ToExcelSerial(dateOnly.ToDateTime(TimeOnly.MinValue)).ToString(CultureInfo.InvariantCulture);
            value = CellExportValue.Number(serial);
            return true;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            var serial = ToExcelSerial(dto.UtcDateTime).ToString(CultureInfo.InvariantCulture);
            value = CellExportValue.Number(serial);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            var serial = ToExcelSerial(dt).ToString(CultureInfo.InvariantCulture);
            value = CellExportValue.Number(serial);
            return true;
        }

        value = CellExportValue.Blank();
        return false;
    }

    private static bool TryConvertBoolean(ReportCellDto cell, out CellExportValue value)
    {
        if (cell.Value is { ValueKind: JsonValueKind.True or JsonValueKind.False } json)
        {
            value = CellExportValue.Boolean(json.GetBoolean());
            return true;
        }

        if (bool.TryParse(cell.Display, out var boolValue))
        {
            value = CellExportValue.Boolean(boolValue);
            return true;
        }

        value = CellExportValue.Blank();
        return false;
    }

    private static bool TryGetDecimal(ReportCellDto cell, out decimal value)
    {
        if (cell.Value is { } json)
        {
            if (json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out value))
                return true;

            if (json.ValueKind == JsonValueKind.String
                && decimal.TryParse(json.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        if (decimal.TryParse(cell.Display, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;

        value = 0m;
        return false;
    }

    private static string? ExtractString(JsonElement? value)
    {
        if (value is null)
            return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.Value.ToString()
        };
    }

    private static uint ResolveStyleId(ReportRowKind rowKind, ReportCellDto cell)
    {
        var valueType = NormalizeValueType(cell.ValueType);
        var isInteger = IsIntegralValueType(valueType);
        var isDecimal = IsNumericValueType(valueType) && !isInteger;
        var isDate = valueType == "date";
        var isDateTime = valueType == "datetime" || valueType == "datetimeoffset";
        var isTime = valueType == "time";

        return rowKind switch
        {
            ReportRowKind.Header => isDate ? 9u : isDateTime ? 10u : isInteger ? 7u : isDecimal ? 8u : 6u,
            ReportRowKind.Group => isInteger ? 12u : isDecimal ? 13u : 11u,
            ReportRowKind.Subtotal => isInteger ? 15u : isDecimal ? 16u : 14u,
            ReportRowKind.Total => isInteger ? 18u : isDecimal ? 19u : 17u,
            _ => isInteger ? 1u : isDecimal ? 2u : isDate ? 3u : isDateTime ? 4u : isTime ? 5u : 0u
        };
    }

    private static string NormalizeValueType(string? valueType)
        => string.IsNullOrWhiteSpace(valueType)
            ? string.Empty
            : valueType.Trim().ToLowerInvariant();

    private static bool IsNumericValueType(string valueType)
        => valueType is "decimal" or "double" or "float" or "single" or "int" or "int32" or "int64" or "long" or "short" or "byte";

    private static bool IsIntegralValueType(string valueType)
        => valueType is "int" or "int32" or "int64" or "long" or "short" or "byte";

    private static bool IsDateValueType(string valueType)
        => valueType is "date" or "datetime" or "datetimeoffset" or "time";

    private static double ToExcelSerial(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return utc.Subtract(new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
    }

    private static string BuildWorksheetXml(WorksheetExport export)
    {
        var worksheet = new XElement(
            NsSpreadsheet + "worksheet",
            new XAttribute(XNamespace.Xmlns + "r", NsRel),
            BuildSheetViews(export),
            new XElement(
                NsSpreadsheet + "sheetFormatPr",
                new XAttribute("defaultRowHeight", "15")),
            BuildColumnsXml(export.ColumnCount),
            new XElement(
                NsSpreadsheet + "sheetData",
                export.Rows.Select(BuildRowXml)));

        if (export.Merges.Count > 0)
        {
            worksheet.Add(new XElement(
                NsSpreadsheet + "mergeCells",
                new XAttribute("count", export.Merges.Count),
                export.Merges.Select(x => new XElement(NsSpreadsheet + "mergeCell", new XAttribute("ref", x)))));
        }

        return ToXmlString(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet));
    }

    private static XElement BuildSheetViews(WorksheetExport export)
    {
        if (export is { HeaderRowCount: <= 0, FrozenColumnCount: <= 0 })
            return new XElement(NsSpreadsheet + "sheetViews", new XElement(NsSpreadsheet + "sheetView", new XAttribute("workbookViewId", "0")));

        var topLeftCell = GetCellReference(Math.Max(1, export.FrozenColumnCount + 1), Math.Max(1, export.HeaderRowCount + 1));
        var pane = new XElement(
            NsSpreadsheet + "pane",
            new XAttribute("state", "frozen"),
            new XAttribute("topLeftCell", topLeftCell));

        if (export.FrozenColumnCount > 0)
            pane.Add(new XAttribute("xSplit", export.FrozenColumnCount));

        if (export.HeaderRowCount > 0)
            pane.Add(new XAttribute("ySplit", export.HeaderRowCount));

        pane.Add(new XAttribute("activePane", export is { FrozenColumnCount: > 0, HeaderRowCount: > 0 }
            ? "bottomRight"
            : export.HeaderRowCount > 0 
                ? "bottomLeft"
                : "topRight"));

        return new XElement(
            NsSpreadsheet + "sheetViews",
            new XElement(
                NsSpreadsheet + "sheetView",
                new XAttribute("workbookViewId", "0"),
                pane));
    }

    private static XElement BuildColumnsXml(int columnCount)
        => new(
            NsSpreadsheet + "cols",
            Enumerable.Range(1, Math.Max(1, columnCount))
                .Select(index => new XElement(
                    NsSpreadsheet + "col",
                    new XAttribute("min", index),
                    new XAttribute("max", index),
                    new XAttribute("width", index <= 2 ? "22" : "16"),
                    new XAttribute("customWidth", "1"))));

    private static XElement BuildRowXml(WorksheetRow row)
        => new(
            NsSpreadsheet + "row",
            new XAttribute("r", row.RowIndex),
            row.Cells.Select(cell => BuildCellXml(cell, row.RowIndex)));

    private static XElement BuildCellXml(WorksheetCell cell, int rowIndex)
    {
        var reference = GetCellReference(cell.ColumnIndex, rowIndex);
        return cell.Value.Kind switch
        {
            CellKind.Blank => new XElement(NsSpreadsheet + "c", new XAttribute("r", reference), new XAttribute("s", cell.StyleId)),
            CellKind.Boolean => new XElement(
                NsSpreadsheet + "c",
                new XAttribute("r", reference),
                new XAttribute("s", cell.StyleId),
                new XAttribute("t", "b"),
                new XElement(NsSpreadsheet + "v", cell.Value.RawValue)),
            CellKind.Number => new XElement(
                NsSpreadsheet + "c",
                new XAttribute("r", reference),
                new XAttribute("s", cell.StyleId),
                new XElement(NsSpreadsheet + "v", cell.Value.RawValue)),
            _ => new XElement(
                NsSpreadsheet + "c",
                new XAttribute("r", reference),
                new XAttribute("s", cell.StyleId),
                new XAttribute("t", "inlineStr"),
                new XElement(NsSpreadsheet + "is", new XElement(NsSpreadsheet + "t", cell.Value.RawValue)))
        };
    }

    private static string BuildStylesXml()
    {
        var styles = new XElement(
            NsSpreadsheet + "styleSheet",
            new XElement(
                NsSpreadsheet + "numFmts",
                new XAttribute("count", "5"),
                new XElement(NsSpreadsheet + "numFmt", new XAttribute("numFmtId", "164"), new XAttribute("formatCode", "#,##0")),
                new XElement(NsSpreadsheet + "numFmt", new XAttribute("numFmtId", "165"), new XAttribute("formatCode", "#,##0.00")),
                new XElement(NsSpreadsheet + "numFmt", new XAttribute("numFmtId", "166"), new XAttribute("formatCode", "yyyy-mm-dd")),
                new XElement(NsSpreadsheet + "numFmt", new XAttribute("numFmtId", "167"), new XAttribute("formatCode", "yyyy-mm-dd hh:mm:ss")),
                new XElement(NsSpreadsheet + "numFmt", new XAttribute("numFmtId", "168"), new XAttribute("formatCode", "hh:mm:ss"))),
            new XElement(
                NsSpreadsheet + "fonts",
                new XAttribute("count", "2"),
                new XElement(NsSpreadsheet + "font",
                    new XElement(NsSpreadsheet + "sz", new XAttribute("val", "11")),
                    new XElement(NsSpreadsheet + "color", new XAttribute("theme", "1")),
                    new XElement(NsSpreadsheet + "name", new XAttribute("val", "Calibri")),
                    new XElement(NsSpreadsheet + "family", new XAttribute("val", "2"))),
                new XElement(NsSpreadsheet + "font",
                    new XElement(NsSpreadsheet + "b"),
                    new XElement(NsSpreadsheet + "sz", new XAttribute("val", "11")),
                    new XElement(NsSpreadsheet + "color", new XAttribute("theme", "1")),
                    new XElement(NsSpreadsheet + "name", new XAttribute("val", "Calibri")),
                    new XElement(NsSpreadsheet + "family", new XAttribute("val", "2")))),
            new XElement(
                NsSpreadsheet + "fills",
                new XAttribute("count", "6"),
                new XElement(NsSpreadsheet + "fill", new XElement(NsSpreadsheet + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(NsSpreadsheet + "fill", new XElement(NsSpreadsheet + "patternFill", new XAttribute("patternType", "gray125"))),
                CreateSolidFill("FFF3F4F6"),
                CreateSolidFill("FFF9FAFB"),
                CreateSolidFill("FFEEF2FF"),
                CreateSolidFill("FFE0E7FF")),
            new XElement(
                NsSpreadsheet + "borders",
                new XAttribute("count", "2"),
                new XElement(NsSpreadsheet + "border",
                    new XElement(NsSpreadsheet + "left"),
                    new XElement(NsSpreadsheet + "right"),
                    new XElement(NsSpreadsheet + "top"),
                    new XElement(NsSpreadsheet + "bottom"),
                    new XElement(NsSpreadsheet + "diagonal")),
                new XElement(NsSpreadsheet + "border",
                    new XElement(NsSpreadsheet + "left"),
                    new XElement(NsSpreadsheet + "right"),
                    new XElement(NsSpreadsheet + "top", new XAttribute("style", "thin"), new XElement(NsSpreadsheet + "color", new XAttribute("rgb", "FFD1D5DB"))),
                    new XElement(NsSpreadsheet + "bottom", new XAttribute("style", "thin"), new XElement(NsSpreadsheet + "color", new XAttribute("rgb", "FFD1D5DB"))),
                    new XElement(NsSpreadsheet + "diagonal"))),
            new XElement(NsSpreadsheet + "cellStyleXfs", new XAttribute("count", "1"), new XElement(NsSpreadsheet + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
            new XElement(
                NsSpreadsheet + "cellXfs",
                new XAttribute("count", "20"),
                CreateXf(0, 0, 0, 0),
                CreateXf(164, 0, 0, 0),
                CreateXf(165, 0, 0, 0),
                CreateXf(166, 0, 0, 0),
                CreateXf(167, 0, 0, 0),
                CreateXf(168, 0, 0, 0),
                CreateXf(0, 1, 2, 1, isCentered: true),
                CreateXf(164, 1, 2, 1, isCentered: true),
                CreateXf(165, 1, 2, 1, isCentered: true),
                CreateXf(166, 1, 2, 1, isCentered: true),
                CreateXf(167, 1, 2, 1, isCentered: true),
                CreateXf(0, 1, 3, 1),
                CreateXf(164, 1, 3, 1),
                CreateXf(165, 1, 3, 1),
                CreateXf(0, 1, 4, 1),
                CreateXf(164, 1, 4, 1),
                CreateXf(165, 1, 4, 1),
                CreateXf(0, 1, 5, 1),
                CreateXf(164, 1, 5, 1),
                CreateXf(165, 1, 5, 1)),
            new XElement(
                NsSpreadsheet + "cellStyles",
                new XAttribute("count", "1"),
                new XElement(NsSpreadsheet + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))));

        return ToXmlString(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), styles));
    }

    private static XElement CreateSolidFill(string rgb)
        => new(
            NsSpreadsheet + "fill",
            new XElement(
                NsSpreadsheet + "patternFill",
                new XAttribute("patternType", "solid"),
                new XElement(NsSpreadsheet + "fgColor", new XAttribute("rgb", rgb)),
                new XElement(NsSpreadsheet + "bgColor", new XAttribute("indexed", "64"))));

    private static XElement CreateXf(int numFmtId, int fontId, int fillId, int borderId, bool isCentered = false)
    {
        var xf = new XElement(
            NsSpreadsheet + "xf",
            new XAttribute("numFmtId", numFmtId),
            new XAttribute("fontId", fontId),
            new XAttribute("fillId", fillId),
            new XAttribute("borderId", borderId),
            new XAttribute("xfId", "0"),
            new XAttribute("applyFont", fontId > 0 ? "1" : "0"),
            new XAttribute("applyFill", fillId > 0 ? "1" : "0"),
            new XAttribute("applyBorder", borderId > 0 ? "1" : "0"),
            new XAttribute("applyNumberFormat", numFmtId > 0 ? "1" : "0"));

        xf.Add(new XElement(
            NsSpreadsheet + "alignment",
            new XAttribute("vertical", "center"),
            new XAttribute("horizontal", isCentered ? "center" : "left"),
            new XAttribute("wrapText", "1")));

        return xf;
    }

    private static string BuildWorkbookXml(string worksheetTitle)
        => ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                NsSpreadsheet + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", NsRel),
                new XElement(NsSpreadsheet + "sheets",
                    new XElement(
                        NsSpreadsheet + "sheet",
                        new XAttribute("name", worksheetTitle),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(NsRel + "id", "rId1"))))));

    private static string BuildWorkbookRelationshipsXml()
        => ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                NsPackageRel + "Relationships",
                new XElement(
                    NsPackageRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(
                    NsPackageRel + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml")))));

    private static string BuildRootRelationshipsXml()
        => ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                NsPackageRel + "Relationships",
                new XElement(
                    NsPackageRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")),
                new XElement(
                    NsPackageRel + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
                    new XAttribute("Target", "docProps/core.xml")),
                new XElement(
                    NsPackageRel + "Relationship",
                    new XAttribute("Id", "rId3"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"),
                    new XAttribute("Target", "docProps/app.xml")))));

    private static string BuildContentTypesXml()
        => ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                NsContentTypes + "Types",
                new XElement(NsContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(NsContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(NsContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", ContentType)),
                new XElement(NsContentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(NsContentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                new XElement(NsContentTypes + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
                new XElement(NsContentTypes + "Override", new XAttribute("PartName", "/docProps/app.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml")))));

    private static string BuildAppXml(string worksheetTitle)
        => ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                NsExtended + "Properties",
                new XElement(NsExtended + "Application", "Microsoft Excel"),
                new XElement(NsExtended + "DocSecurity", "0"),
                new XElement(NsExtended + "ScaleCrop", "false"),
                new XElement(NsExtended + "HeadingPairs",
                    new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "vector",
                        new XAttribute("size", "2"),
                        new XAttribute("baseType", "variant"),
                        new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "variant",
                            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "lpstr", "Worksheets")),
                        new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "variant",
                            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "i4", "1")))),
                new XElement(NsExtended + "TitlesOfParts",
                    new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "vector",
                        new XAttribute("size", "1"),
                        new XAttribute("baseType", "lpstr"),
                        new XElement(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes") + "lpstr", worksheetTitle))),
                new XElement(NsExtended + "Company", "OpenAI"),
                new XElement(NsExtended + "LinksUpToDate", "false"),
                new XElement(NsExtended + "SharedDoc", "false"),
                new XElement(NsExtended + "HyperlinksChanged", "false"),
                new XElement(NsExtended + "AppVersion", "1.0"))));

    private string BuildCoreXml(string title)
    {
        var now = _timeProvider.GetUtcNowDateTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return ToXmlString(new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                XNamespace.Get("http://schemas.openxmlformats.org/package/2006/metadata/core-properties") + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "dc", NsDc),
                new XAttribute(XNamespace.Xmlns + "dcterms", NsDcterms),
                new XAttribute(XNamespace.Xmlns + "dcmitype", NsDcmiType),
                new XAttribute(XNamespace.Xmlns + "xsi", NsXsi),
                new XElement(NsDc + "title", title),
                new XElement(NsDc + "creator", "OpenAI"),
                new XElement(NsDcterms + "created", new XAttribute(NsXsi + "type", "dcterms:W3CDTF"), now),
                new XElement(NsDcterms + "modified", new XAttribute(NsXsi + "type", "dcterms:W3CDTF"), now))));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string ToXmlString(XDocument document)
    {
        using var writer = new Utf8StringWriter();
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static string GetCellReference(int columnIndex, int rowIndex)
        => $"{GetColumnName(columnIndex)}{rowIndex}";

    private static string GetColumnName(int columnIndex)
    {
        var dividend = columnIndex;
        var builder = new StringBuilder();
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            builder.Insert(0, (char)('A' + modulo));
            dividend = (dividend - modulo - 1) / 26;
        }

        return builder.ToString();
    }

    private static string GetOccupiedKey(int rowIndex, int columnIndex) => $"{rowIndex}:{columnIndex}";

    private static string SanitizeWorksheetTitle(string? title)
    {
        var raw = string.IsNullOrWhiteSpace(title) ? "Report" : title.Trim();
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        foreach (var ch in invalid)
            raw = raw.Replace(ch, '-');

        return raw.Length > 31 ? raw[..31] : raw;
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private sealed record WorksheetExport(
        IReadOnlyList<WorksheetRow> Rows,
        IReadOnlyList<string> Merges,
        int ColumnCount,
        int HeaderRowCount,
        int FrozenColumnCount);

    private sealed record WorksheetRow(int RowIndex, IReadOnlyList<WorksheetCell> Cells);
    private sealed record WorksheetCell(int ColumnIndex, CellExportValue Value, uint StyleId);

    private sealed record CellExportValue(CellKind Kind, string RawValue)
    {
        public static CellExportValue Blank() => new(CellKind.Blank, string.Empty);
        public static CellExportValue InlineString(string value) => new(CellKind.InlineString, value);
        public static CellExportValue Number(string value) => new(CellKind.Number, value);
        public static CellExportValue Boolean(bool value) => new(CellKind.Boolean, value ? "1" : "0");
    }

    private enum CellKind
    {
        Blank = 0,
        InlineString = 1,
        Number = 2,
        Boolean = 3
    }
}
