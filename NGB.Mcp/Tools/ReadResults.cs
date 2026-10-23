using NGB.Contracts.Accounting;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Contracts.WorkCenter;

namespace NGB.Mcp.Tools;

public sealed record MetadataSummary(McpSourceKind Kind, string TypeCode, string Name);

public sealed record MetadataPage(IReadOnlyList<MetadataSummary> Items, int Offset, int Limit, bool HasMore);

public sealed record MetadataResult(
    CatalogTypeMetadataDto? Catalog = null,
    DocumentTypeMetadataDto? Document = null,
    ReportDefinitionDto? Report = null);

public sealed record McpRecordSource(McpSourceKind Kind, string TypeCode, Guid Id, string? OpenInNgb);

public sealed record RecordLookupField(string Key, string Label, LookupSourceDto Lookup);

public sealed record RecordResult(
    CatalogItemDto? Catalog = null,
    DocumentDto? Document = null,
    GeneralJournalEntryDetailsDto? GeneralJournalEntry = null,
    McpRecordSource? Source = null,
    string? TypeName = null,
    IReadOnlyList<RecordLookupField>? LookupFields = null);

public sealed record RecordsResult(
    PageResponseDto<CatalogItemDto>? Catalogs = null,
    PageResponseDto<DocumentDto>? Documents = null,
    GeneralJournalEntryPageDto? GeneralJournalEntries = null);

public sealed record EffectsResult(
    DocumentEffectsDto Effects,
    int PerSectionLimit,
    string Completeness,
    string IdentifierScope,
    McpRecordSource Source);

public sealed record FlowResult(
    RelationshipGraphDto Graph,
    McpRecordSource Source,
    IReadOnlyDictionary<string, string?> Links);

public sealed record ReportSummary(string ReportCode, string Name, string? Group, string? Description);

public sealed record ReportsResult(IReadOnlyList<ReportSummary> Items, int Offset, int Limit, bool HasMore);

public sealed record WorkItemsResult(WorkCenterSummaryDto? Summary = null, WorkCenterPageDto? Page = null);

public sealed record CapabilitiesResult(
    string Version,
    bool ReadOnly,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tools);

public sealed record SearchItem(string Id, string Title, string Kind, string TypeCode, Guid? RecordId, string? Url);

public sealed record SearchResult(
    IReadOnlyList<SearchItem> Items,
    bool Truncated,
    IReadOnlyList<string> UnavailableSources);

public sealed record CompatibilitySearchItem(string Id, string Title, string? Url);

public sealed record CompatibilitySearchResult(IReadOnlyList<CompatibilitySearchItem> Results);

public sealed record FetchResult(string Id, string Title, string Text, string? Url);
