using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Contracts.WorkCenter;
using NGB.Core.AuditLog;

namespace NGB.Mcp.Tools;

public sealed record EmptyReadRequest;

public enum McpSourceKind
{
    [JsonStringEnumMemberName(NgbSourceKinds.Catalog)]
    Catalog,
    [JsonStringEnumMemberName(NgbSourceKinds.Document)]
    Document,
    [JsonStringEnumMemberName(NgbSourceKinds.GeneralJournalEntry)]
    GeneralJournalEntry,
    [JsonStringEnumMemberName(NgbSourceKinds.Report)]
    Report
}

public sealed record MetadataListRequest(
    McpSourceKind? Kind = null,
    [property: Range(0, 10_000)] int Offset = 0,
    [property: Range(1, 100)] int Limit = 50);

public sealed record MetadataRequest(
    McpSourceKind Kind,
    [property: Required, StringLength(128)] string TypeCode);

public sealed record RecordRequest(
    McpSourceKind Kind,
    [property: Required, StringLength(128)] string TypeCode,
    Guid Id);

public sealed record RecordsRequest(
    McpSourceKind Kind,
    [property: Required, StringLength(128)] string TypeCode,
    [property: Range(1, 100)] int Limit = 50,
    [property: StringLength(16_384)] string? Cursor = null,
    [property: StringLength(256), Description("Optional text search within this type. Available independently of declared field filters.")] string? Search = null,
    [property: Description("Declared list filter codes mapped directly to string values, for example {\"customer_id\":\"record UUID\"}. Unlike report filters, do not wrap values in objects. Multi-select filters use comma-separated values.")]
    IReadOnlyDictionary<string, string>? Filters = null,
    [property: Description("Request the exact count of records matching search and filters. Use limit 1 for count-only questions; read total, not items.length. Null total means unavailable.")]
    bool IncludeTotal = false);

public sealed record FlowRequest(
    [property: Required, StringLength(128)] string TypeCode,
    Guid Id,
    [property: Range(0, 5)] int Depth = 2,
    [property: Range(1, 200)] int MaxNodes = 100);

public sealed record EffectsRequest(
    [property: Required, StringLength(128)] string TypeCode,
    Guid Id,
    [property: Range(1, 100)] int Limit = 50);

public sealed record ReportsRequest(
    [property: Range(0, 10_000)] int Offset = 0,
    [property: Range(1, 100)] int Limit = 50);

public sealed record ReportDefinitionRequest([property: Required, StringLength(128)] string ReportCode);

public sealed record ReportReadRequest(
    [property: Required, StringLength(128)] string ReportCode,
    [property: Description("Report options. Filters are a map keyed by the actual field codes from the report definition. "
        + "Example request: {\"filters\":{\"customer_id\":{\"value\":\"UUID returned by search\"}}}. "
        + "Replace customer_id with the report's filter code. Do not use a literal fieldCode key or an array of filters. "
        + "For lookup filters, value is a record UUID returned by search. Parameters map declared parameter codes to strings, "
        + "including boolean and numeric values (for example \"true\" and \"10\", not JSON true or 10). "
        + "For dates, use yyyy-MM-dd strings. A date inside a record name or identifier is not a date request. "
        + "Only pass optional scope parameters requested by the user or supplied in page criteria; otherwise omit them. "
        + "Declared output controls may select the requested totals or breakdown without changing the scope.")]
    ReportExecutionRequestDto Request);

public sealed record AuditReadRequest(
    AuditEntityKind Kind,
    Guid Id,
    DateTime? AfterOccurredAtUtc = null,
    Guid? AfterAuditEventId = null,
    [property: Range(1, 100)] int Limit = 50);

public sealed record WorkItemsRequest(WorkCenterQueryDto? Query = null, bool SummaryOnly = false);

public sealed record ContentSummaryRequest(BusinessObjectRef Target);

public sealed record ContentListRequest(
    BusinessObjectRef Target,
    [property: Range(1, 100)] int Limit = 50,
    Guid? Cursor = null);

public sealed record SearchRequest(
    [property: Required, StringLength(256, MinimumLength = 1)] string Query,
    [property: Range(1, 50)] int Limit = 20,
    NgbSearchKind? Kind = null,
    [property: StringLength(128, MinimumLength = 1)] string? TypeCode = null);

public sealed record CompatibilitySearchRequest([property: Required, StringLength(256, MinimumLength = 1)] string Query);

public sealed record FetchRequest([property: Required, StringLength(256)] string Id);
