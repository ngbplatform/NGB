using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Contracts.Audit;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Mcp;
using NGB.Mcp.Tools;

namespace NGB.AiAssistant.AspNetCore;

/// <summary>Removes UI layout overhead from authorized MCP results without changing their business scope.</summary>
internal static class McpAssistantProjection
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static JsonElement Create(string tool, JsonElement data, IDictionary<string, object?> arguments)
    {
        object result = tool switch
        {
            McpToolNames.GetMetadata => Metadata(data.Deserialize<MetadataResult>(Json)!),
            McpToolNames.GetReportDefinition => Definition(data.Deserialize<ReportDefinitionDto>(Json)!),
            McpToolNames.ExecuteReport => Report(data.Deserialize<ReportExecutionResponseDto>(Json)!, arguments),
            McpToolNames.ListMetadata => MetadataPage(data.Deserialize<MetadataPage>(Json)!),
            McpToolNames.ListReports => Reports(data.Deserialize<ReportsResult>(Json)!),
            McpToolNames.GetAuditHistory => Audit(data.Deserialize<AuditLogPageDto>(Json)!),
            _ => data
        };

        return JsonSerializer.SerializeToElement(result, Json);
    }

    private static object Metadata(MetadataResult metadata)
        => metadata switch
        {
            { Document: { } document } => new
            {
                kind = "document",
                typeCode = document.DocumentType,
                name = document.DisplayName,
                fields = Fields(document.List),
                formFields = FormFields(document.Form),
                filters = document.List?.Filters ?? [],
                parts = document.Parts?.Select(part => new { part.PartCode, part.Title, fields = Fields(part.List) }),
                filtering = "Only the declared list filters are supported. Field presence does not imply filter support."
            },
            { Catalog: { } catalog } => new
            {
                kind = "catalog",
                typeCode = catalog.CatalogType,
                name = catalog.DisplayName,
                fields = Fields(catalog.List),
                formFields = FormFields(catalog.Form),
                filters = catalog.List?.Filters ?? [],
                parts = catalog.Parts?.Select(part => new { part.PartCode, part.Title, fields = Fields(part.List) }),
                filtering = "Only the declared list filters are supported. Field presence does not imply filter support."
            },
            { Report: { } report } => Definition(report),
            _ => metadata
        };

    private static object? Fields(ListMetadataDto? list)
        => list?.Columns.Select(column => new
        {
            column.Key,
            column.Label,
            column.DataType,
            column.Lookup,
            column.Options
        });

    private static object? FormFields(FormMetadataDto? form)
        => form?.Sections
            .SelectMany(section => section.Rows)
            .SelectMany(row => row.Fields)
            .Select(field => new
            {
                field.Key,
                field.Label,
                field.DataType,
                field.Lookup,
                field.Options
            });

    private static object Definition(ReportDefinitionDto definition)
        => new
        {
            definition.ReportCode,
            definition.Name,
            definition.Description,
            definition.Mode,
            definition.Dataset,
            parameters = definition.Parameters ?? [],
            filters = definition.Filters ?? [],
            definition.DefaultLayout,
            definition.Capabilities,
            scope = "Required filters restrict the report scope. Do not omit them or substitute a different scope."
        };

    private static object MetadataPage(MetadataPage page)
        => new
        {
            page.Items,
            page.Offset,
            page.Limit,
            page.HasMore,
            counts = !page.HasMore && page.Offset == 0
                ? page.Items
                    .GroupBy(item => item.Kind.ToCode())
                    .ToDictionary(group => group.Key, group => group.Count())
                : null
        };

    private static object Reports(ReportsResult page)
        => new
        {
            page.Items,
            page.Offset,
            page.Limit,
            page.HasMore,
            count = page is { HasMore: false, Offset: 0 }
                ? (int?)page.Items.Count
                : null
        };

    private static object Audit(AuditLogPageDto page)
        => new
        {
            page.Limit,
            page.NextCursor,
            items = page.Items.Select(item => new
            {
                item.AuditEventId,
                item.OccurredAtUtc,
                item.Actor,
                action = Label(item.ActionCode),
                metadata = ReadJson(item.MetadataJson),
                changes = item.Changes
                    .Where(change => !change.FieldPath.EndsWith("updated_at_utc", StringComparison.OrdinalIgnoreCase))
                    .Select(change => new
                    {
                        field = Label(change.FieldPath),
                        before = ReadJson(change.OldValueJson),
                        after = ReadJson(change.NewValueJson)
                    })
            })
        };

    private static string Label(string value)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value
            .Replace("_utc", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Replace('.', ' '));

    private static JsonElement? ReadJson(string? value)
    {
        if (value is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(value);
        }
    }

    private static object Report(ReportExecutionResponseDto report, IDictionary<string, object?> arguments)
        => new
        {
            criteria = arguments,
            report.Offset,
            report.Limit,
            report.Total,
            report.HasMore,
            report.NextCursor,
            report.Sheet.Meta?.Title,
            report.Sheet.Meta?.Subtitle,
            report.Sheet.Meta?.IsPivot,
            report.Sheet.Meta?.HasColumnGroups,
            sheetDiagnostics = report.Sheet.Meta?.Diagnostics,
            columns = report.Sheet.Columns.Select(column => new { column.Code, column.Title, column.DataType, column.SemanticRole }),
            headerRows = report.Sheet.HeaderRows is { } headers ? Rows(headers) : null,
            rows = Rows(report.Sheet.Rows),
            report.Diagnostics
        };

    private static object Rows(IReadOnlyList<ReportSheetRowDto> rows)
        => rows.Select(row => new
        {
            row.RowKind,
            row.OutlineLevel,
            row.IsExpanded,
            row.GroupKey,
            row.SemanticRole,
            row.ChildrenPath,
            cells = row.Cells.Select(cell => new
            {
                cell.Value,
                cell.Display,
                cell.ValueType,
                cell.SemanticRole,
                cell.ColSpan,
                cell.RowSpan
            })
        });
}
