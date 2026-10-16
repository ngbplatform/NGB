using System.ComponentModel.DataAnnotations;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Reporting;
using NGB.Contracts.WorkCenter;
using NGB.Core.AuditLog;

namespace NGB.Mcp.Tools;

public sealed record EmptyReadRequest;

public enum McpSourceKind
{
    Catalog,
    Document,
    GeneralJournalEntry,
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
    [property: StringLength(256)] string? Search = null,
    IReadOnlyDictionary<string, string>? Filters = null);

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
