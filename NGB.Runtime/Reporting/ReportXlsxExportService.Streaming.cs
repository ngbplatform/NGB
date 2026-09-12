using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed partial class ReportXlsxExportService
{
    public Task WriteXlsxAsync(
        Stream output,
        ReportSheetDto template,
        IAsyncEnumerable<ReportSheetRowDto> rows,
        CancellationToken ct)
        => WriteXlsxAsync(output, template, rows, 1_048_576, ct);

    internal async Task WriteXlsxAsync(
        Stream output,
        ReportSheetDto template,
        IAsyncEnumerable<ReportSheetRowDto> rows,
        int rowsPerWorksheet,
        CancellationToken ct)
    {
        var headers = template.HeaderRows?.Count > 0
            ? template.HeaderRows
            : [BuildFlatHeaderRow(template.Columns)];

        if (rowsPerWorksheet <= headers.Count)
            throw new ArgumentOutOfRangeException(nameof(rowsPerWorksheet));

        await using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var titles = new List<string>();
        var title = SanitizeWorksheetTitle(template.Meta?.Title);
        await using var enumerator = rows.GetAsyncEnumerator(ct);
        var hasRow = await enumerator.MoveNextAsync();

        do
        {
            ct.ThrowIfCancellationRequested();

            var sheetNumber = titles.Count + 1;
            var suffix = sheetNumber == 1 ? "" : $" ({sheetNumber})";
            titles.Add(title[..Math.Min(title.Length, 31 - suffix.Length)] + suffix);

            await using var entry = archive.CreateEntry($"xl/worksheets/sheet{sheetNumber}.xml", CompressionLevel.Fastest).Open();
            await using var writer = XmlWriter.Create(entry, new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), CloseOutput = false });
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "worksheet", NsSpreadsheet.NamespaceName);

            var merges = new List<string>();

            var mergeOptions = new FileStreamOptions 
            { 
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            };

            if (!OperatingSystem.IsWindows())
                mergeOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using var mergeFile = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), mergeOptions);
            await using var mergeWriter = new StreamWriter(mergeFile, new UTF8Encoding(false), leaveOpen: true);

            var occupied = new HashSet<string>();
            var worksheet = new WorksheetExport([], merges, template.Columns.Count, headers.Count, CountFrozenColumns(template.Columns));

            await BuildSheetViews(worksheet).WriteToAsync(writer, ct);
            await BuildColumnsXml(template.Columns.Count).WriteToAsync(writer, ct);
            await writer.WriteStartElementAsync(null, "sheetData", NsSpreadsheet.NamespaceName);

            var index = 1;

            foreach (var header in headers)
            {
                await BuildRowXml(BuildWorksheetRow(header, index++, template.Columns.Count, merges, occupied)).WriteToAsync(writer, ct);
            }
            
            foreach (var merge in merges)
            {
                await mergeWriter.WriteLineAsync(merge.AsMemory(), ct);
            }

            merges.Clear();
            occupied.Clear();
            
            while (hasRow && index <= rowsPerWorksheet)
            {
                ct.ThrowIfCancellationRequested();

                var row = enumerator.Current;
                if (row.Cells.Any(c => c.RowSpan > 1))
                    throw new NgbInvariantViolationException("Report data rows must not span multiple rows.");

                await BuildRowXml(BuildWorksheetRow(row, index++, template.Columns.Count, merges, occupied)).WriteToAsync(writer, ct);
                foreach (var merge in merges)
                {
                    await mergeWriter.WriteLineAsync(merge.AsMemory(), ct);
                }

                merges.Clear();
                occupied.Clear();

                hasRow = await enumerator.MoveNextAsync();
            }

            await writer.WriteEndElementAsync();
            await mergeWriter.FlushAsync(ct);

            if (mergeFile.Length > 0)
            {
                mergeFile.Position = 0;
                using var mergeReader = new StreamReader(mergeFile, leaveOpen: true);
                await writer.WriteStartElementAsync(null, "mergeCells", NsSpreadsheet.NamespaceName);

                while (await mergeReader.ReadLineAsync(ct) is { } merge)
                {
                    await new XElement(NsSpreadsheet + "mergeCell", new XAttribute("ref", merge)).WriteToAsync(writer, ct);
                }

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        } while (hasRow);

        var workbook = XDocument.Parse(BuildWorkbookXml(titles[0]));
        workbook.Root!.Element(NsSpreadsheet + "sheets")!.ReplaceNodes(titles.Select((t, i)
            => new XElement(NsSpreadsheet + "sheet",
                new XAttribute("name", t),
                new XAttribute("sheetId", i + 1),
                new XAttribute(NsRel + "id", $"rId{i + 1}"))));

        var relationships = XDocument.Parse(BuildWorkbookRelationshipsXml());
        relationships.Root!.ReplaceNodes(titles.Select((_, i)
            => new XElement(NsPackageRel + "Relationship",
                new XAttribute("Id", $"rId{i + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i + 1}.xml"))));
        relationships.Root.Add(
            new XElement(NsPackageRel + "Relationship",
            new XAttribute("Id", $"rId{titles.Count + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));

        var contentTypes = XDocument.Parse(BuildContentTypesXml());
        for (var i = 2; i <= titles.Count; i++)
        {
            contentTypes.Root!.Add(
                new XElement(NsContentTypes + "Override",
                    new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }

        WriteEntry(archive, "[Content_Types].xml", ToXmlString(contentTypes));
        WriteEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
        WriteEntry(archive, "docProps/core.xml", BuildCoreXml(template.Meta?.Title ?? title));
        WriteEntry(archive, "xl/workbook.xml", ToXmlString(workbook));
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", ToXmlString(relationships));
        WriteEntry(archive, "xl/styles.xml", BuildStylesXml());
        
        var app = XDocument.Parse(BuildAppXml(title));
        var vt = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        app.Descendants(vt + "i4").Single().Value = titles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var titleVector = app.Root!.Element(NsExtended + "TitlesOfParts")!.Element(vt + "vector")!;
        titleVector.SetAttributeValue("size", titles.Count);
        titleVector.ReplaceNodes(titles.Select(t => new XElement(vt + "lpstr", t)));
        WriteEntry(archive, "docProps/app.xml", ToXmlString(app));
    }
}
