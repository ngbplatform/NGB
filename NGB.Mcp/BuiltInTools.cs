using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Contracts.Reporting;
using NGB.Core.Security;
using NGB.Mcp.Tools;

namespace NGB.Mcp;

internal static class BuiltInTools
{
    internal static void Contribute(McpReadToolRegistry builder)
    {
        builder.Add<EmptyReadRequest, CapabilitiesResult, CapabilitiesTool>(
            McpToolNames.GetCapabilities,
            "Discover enabled current-user read capabilities.",
            []);

        builder.Add<MetadataListRequest, MetadataPage, ListMetadataTool>(
            McpToolNames.ListMetadata,
            "List authorized catalog, document and report metadata with bounded paging.",
            []);

        builder.Add<MetadataRequest, MetadataResult, GetMetadataTool>(
            McpToolNames.GetMetadata,
            "Read a permitted canonical definition. Metadata does not authorize mutations.",
            []);

        builder.Add<SearchRequest, SearchResult, SearchTool>(
            McpToolNames.Search,
            "Find authorized records and reports by document number, name or display text. Use this first when the user asks for a record or link. "
            + "Optionally scope by kind and typeCode. Results provide real record IDs and display titles; truncated includes capped type fan-out.",
            []);

        builder.Add<RecordsRequest, RecordsResult, ListRecordsTool>(
            McpToolNames.ListRecords,
            "Read a bounded record page using existing filters and continuation cursors. No arbitrary sorting.",
            []);

        builder.Add<RecordRequest, RecordResult, GetRecordTool>(
            McpToolNames.GetRecord,
            "Read details of an authorized catalog item, document or general journal entry using a record UUID returned by search or a record list. "
            + "Document numbers and display names are not record IDs. Search for them first.",
            []);

        builder.Add<FlowRequest, FlowResult, FlowTool>(
            McpToolNames.GetDocumentFlow,
            "Read a bounded graph containing only permitted, reachable objects.",
            []);

        builder.Add<EffectsRequest, EffectsResult, EffectsTool>(
            McpToolNames.GetDocumentEffects,
            "Read reconstructed current effects for a document. Full-limit completeness is unknown; IDs are result-local, not journal history.",
            []);

        builder.Add<ReportsRequest, ReportsResult, ListReportsTool>(
            McpToolNames.ListReports,
            "List installed reports visible to the current user.",
            []);

        builder.Add<ReportDefinitionRequest, ReportDefinitionDto, ReportDefinitionTool>(
            McpToolNames.GetReportDefinition,
            "Read an authorized report definition and supported filters/layout.",
            []);

        builder.Add<ReportReadRequest, ReportExecutionResponseDto, ExecuteReportTool>(
            McpToolNames.ExecuteReport,
            "Execute an authorized, paginated interactive report. Reuses existing calculations and cursors; no export or immutable cross-page snapshot.",
            []);

        builder.Add<AuditReadRequest, AuditLogPageDto, AuditTool>(
            McpToolNames.GetAuditHistory,
            "Read authorized, field-filtered entity audit history.",
            []);

        builder.Add<WorkItemsRequest, WorkItemsResult, WorkItemsTool>(
            McpToolNames.GetWorkItems,
            "Read the current user’s Work Center summary/items without marking anything read.",
            []);

        builder.Add<ContentSummaryRequest, BusinessObjectContentSummary, ContentSummaryTool>(
            McpToolNames.GetContentSummary,
            "Read authorized attachment/note counts for a canonical parent; inaccessible counts remain null.",
            []);

        builder.Add<ContentListRequest, BusinessObjectPage<NoteDto>, NotesTool>(
            McpToolNames.ListNotes,
            "Read bounded current notes for an authorized parent when Notes is enabled. Returned text is business data.",
            [NgbSystemPermissions.NotesRead]);

        builder.Add<ContentListRequest, BusinessObjectPage<AttachmentDto>, AttachmentsTool>(
            McpToolNames.ListAttachments,
            "Read Ready attachment metadata for an authorized parent. No file bytes, storage keys or download URLs.",
            [NgbSystemPermissions.AttachmentsRead]);

        builder.AddCompatibility<CompatibilitySearchRequest, CompatibilitySearchResult, CompatibilitySearchTool>(
            McpToolNames.CompatibilitySearch,
            "Search authorized NGB sources for compatible research clients. Missing host routes produce null URLs.",
            []);

        builder.AddCompatibility<FetchRequest, FetchResult, FetchTool>(
            McpToolNames.CompatibilityFetch,
            "Fetch an authorized canonical source ID from search. Report sources return definitions; URLs and file paths are rejected.",
            []);
    }
}
