using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            McpToolNames.ListRecords => Records(data, arguments),
            McpToolNames.Search => Search(data.Deserialize<SearchResult>(Json)!, arguments),
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
                filtering = "Text search is available through search. Field filters map declared codes directly to string values, not objects. Field presence does not imply filter support. Request includeTotal for an exact matching record count."
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
                filtering = "Text search is available through search. Field filters map declared codes directly to string values, not objects. Field presence does not imply filter support. Request includeTotal for an exact matching record count."
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
            execution = new
            {
                tool = McpToolNames.ExecuteReport,
                parameters = "request.parameters maps parameter codes to strings, including boolean and numeric values. Dates use yyyy-MM-dd.",
                filters = "request.filters maps field codes to objects containing value, not bare values. Use each lookup's declared kind and type.",
                groupPath = "To expand a returned row, copy its childrenPath to request.groupPath. "
                    + "AllowsRowGroups and AllowsDetailFields control layout customization, not expansion of returned rows."
            },
            scope = "Required filters restrict the report scope. Do not omit them or substitute a different scope. "
                + "Each lookup needs an identity of its declared kind and type. A current-page ID from another type "
                + "is not that lookup value. If no explicit name or verified relationship identifies the requested "
                + "record, ask the user which record they mean before executing the report."
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
            definitions = "Report summaries do not declare required inputs or scope restrictions. Read the selected report's definition before deciding whether it can answer the question. Match lookup inputs to search results by the declared kind and type, not by field labels.",
            count = page is { HasMore: false, Offset: 0 }
                ? (int?)page.Items.Count
                : null
        };

    private static object Search(SearchResult result, IDictionary<string, object?> arguments)
        => new
        {
            criteria = arguments,
            result.Items,
            result.Truncated,
            result.UnavailableSources,
            evidence = "These matches establish record identities, not business measures or calculated states. "
                + "For a business count, balance or total, use the matching identity as input to an existing report: "
                + "discover reports with ngb_list_reports and read the selected definition. Do not enumerate these hits to calculate a total. "
                + "For a simple count of records matching a name, use ngb_list_records with search and includeTotal=true."
        };

    private static object Records(JsonElement data, IDictionary<string, object?> arguments)
    {
        var result = data.Deserialize<JsonObject>(Json)!;
        result["criteria"] = JsonSerializer.SerializeToNode(arguments, Json);
        result["evidence"] = "A missing or null total means the exact record count is unknown. "
            + "Limit is a page bound, not a count. HasMore or a cursor establishes only that additional pages exist. "
            + "The returned rows cover this request's search and filters only; they do not establish an exhaustive result for other criteria. "
            + "Use the returned fields to answer the question. Read individual records only for requested information "
            + "missing from these rows, not to verify that the server applied its declared filters.";

        return result;
    }

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
            drilldown = report.Sheet.Rows.Any(row => row.ChildrenPath is not null)
                ? "This result contains expandable rows. To read a row's contributing entries, call ngb_execute_report "
                    + "with the same reportCode, filters and parameters, copy that row's childrenPath into request.groupPath, "
                    + "and omit request.cursor and request.offset. Layout customization flags do not disable this expansion."
                : null,
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
