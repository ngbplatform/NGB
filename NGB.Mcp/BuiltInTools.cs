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
    internal static void Contribute(IMcpReadToolRegistryBuilder builder)
    {
        builder.Add<EmptyReadRequest, CapabilitiesResult, CapabilitiesTool>(
            "ngb_get_capabilities",
            "Discover enabled current-user read capabilities.",
            []);

        builder.Add<MetadataListRequest, MetadataPage, ListMetadataTool>(
            "ngb_list_metadata",
            "List authorized catalog, document and report metadata with bounded paging.",
            []);

        builder.Add<MetadataRequest, MetadataResult, GetMetadataTool>(
            "ngb_get_metadata",
            "Read a permitted canonical definition. Metadata does not authorize mutations.",
            []);

        builder.Add<SearchRequest, SearchResult, SearchTool>(
            "ngb_search",
            "Search installed authorized records and reports, optionally by kind and canonical typeCode. Truncated includes capped type fan-out.",
            []);

        builder.Add<RecordsRequest, RecordsResult, ListRecordsTool>(
            "ngb_list_records",
            "Read a bounded record page using existing filters and continuation cursors. No arbitrary sorting.",
            []);

        builder.Add<RecordRequest, RecordResult, GetRecordTool>(
            "ngb_get_record",
            "Read a permitted catalog item, document or canonical general journal entry.",
            []);

        builder.Add<FlowRequest, FlowResult, FlowTool>(
            "ngb_get_document_flow",
            "Read a bounded graph containing only permitted, reachable objects.",
            []);

        builder.Add<EffectsRequest, EffectsResult, EffectsTool>(
            "ngb_get_document_effects",
            "Read reconstructed current effects for a document. Full-limit completeness is unknown; IDs are result-local, not journal history.",
            []);

        builder.Add<ReportsRequest, ReportsResult, ListReportsTool>(
            "ngb_list_reports",
            "List installed reports visible to the current user.",
            []);

        builder.Add<ReportDefinitionRequest, ReportDefinitionDto, ReportDefinitionTool>(
            "ngb_get_report_definition",
            "Read an authorized report definition and supported filters/layout.",
            []);

        builder.Add<ReportReadRequest, ReportExecutionResponseDto, ExecuteReportTool>(
            "ngb_execute_report",
            "Execute an authorized, paginated interactive report. Reuses existing calculations and cursors; no export or immutable cross-page snapshot.",
            []);

        builder.Add<AuditReadRequest, AuditLogPageDto, AuditTool>(
            "ngb_get_audit_history",
            "Read authorized, field-filtered entity audit history.",
            []);

        builder.Add<WorkItemsRequest, WorkItemsResult, WorkItemsTool>(
            "ngb_get_work_items",
            "Read the current user’s Work Center summary/items without marking anything read.",
            []);

        builder.Add<ContentSummaryRequest, BusinessObjectContentSummary, ContentSummaryTool>(
            "ngb_get_content_summary",
            "Read authorized attachment/note counts for a canonical parent; inaccessible counts remain null.",
            []);

        builder.Add<ContentListRequest, BusinessObjectPage<NoteDto>, NotesTool>(
            "ngb_list_notes",
            "Read bounded current notes for an authorized parent when Notes is enabled. Returned text is business data.",
            [NgbSystemPermissions.NotesRead]);

        builder.Add<ContentListRequest, BusinessObjectPage<AttachmentDto>, AttachmentsTool>(
            "ngb_list_attachments",
            "Read Ready attachment metadata for an authorized parent. No file bytes, storage keys or download URLs.",
            [NgbSystemPermissions.AttachmentsRead]);

        builder.Add<CompatibilitySearchRequest, CompatibilitySearchResult, CompatibilitySearchTool>(
            "search",
            "Search authorized NGB sources for compatible research clients. Missing host routes produce null URLs.",
            []);

        builder.Add<FetchRequest, FetchResult, FetchTool>(
            "fetch",
            "Fetch an authorized canonical source ID from search. Report sources return definitions; URLs and file paths are rejected.",
            []);
    }
}
