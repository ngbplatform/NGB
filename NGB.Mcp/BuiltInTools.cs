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
            + "Scope by kind and typeCode only when known from metadata or context; otherwise omit them. Retry without scope if a scoped search is empty. "
            + "Results provide real record IDs and display titles; truncated includes capped type fan-out. "
            + "Search discovers matching records; the number of hits is not an aggregate business count.",
            []);

        builder.Add<RecordsRequest, RecordsResult, ListRecordsTool>(
            McpToolNames.ListRecords,
            "List records of a known type (including requests for several examples). Discover the type and its kind in metadata first. "
            + "Use declared list filters and continuation cursors. Search is for finding named records, not enumerating all records. "
            + "A page is not the complete population while a continuation is present. No arbitrary sorting.",
            []);

        builder.Add<RecordRequest, RecordResult, GetRecordTool>(
            McpToolNames.GetRecord,
            "Read details of an authorized catalog item, document or general journal entry using a record UUID returned by search or a record list. "
            + "Document numbers and display names are not record IDs. Search for them first.",
            []);

        builder.Add<FlowRequest, FlowResult, FlowTool>(
            McpToolNames.GetDocumentFlow,
            "Read a bounded graph containing only permitted, reachable objects. Use it to follow related documents and completion records. "
            + "A posted document is recorded in the system; posting alone does not establish whether the business process is finished. "
            + "Read related document details to establish the actual outcome. An incomplete graph cannot prove that no related documents exist.",
            []);

        builder.Add<EffectsRequest, EffectsResult, EffectsTool>(
            McpToolNames.GetDocumentEffects,
            "Read reconstructed current effects for a document. Full-limit completeness is unknown; IDs are result-local, not journal history.",
            []);

        builder.Add<ReportsRequest, ReportsResult, ListReportsTool>(
            McpToolNames.ListReports,
            "Discover installed reports visible to the current user: compact names and descriptions with paging. "
            + "Read the selected report's definition for its inputs before executing it. This returns metadata, not report figures.",
            []);

        builder.Add<ReportDefinitionRequest, ReportDefinitionDto, ReportDefinitionTool>(
            McpToolNames.GetReportDefinition,
            "Read an authorized report definition, required filters, optional parameters and lookup sources. "
            + "Resolve required lookup IDs through search using the declared lookup type. This returns metadata, not report figures.",
            []);

        builder.Add<ReportReadRequest, ReportExecutionResponseDto, ExecuteReportTool>(
            McpToolNames.ExecuteReport,
            "Execute an authorized report to obtain actual business figures. Read its definition and resolve required filters first. "
            + "Filters use actual field codes as keys: request.filters = {\"customer_id\":{\"value\":\"record UUID\"}}. "
            + "Replace customer_id with the field code declared by the report; never pass a bare UUID or a literal fieldCode key. "
            + "Date inputs go in request.parameters as yyyy-MM-dd strings, not in filters or at the request root. "
            + "Omit optional date parameters when no period was requested. Never extract a date from a record name or identifier. "
            + "Reuses existing calculations and pagination; no export or immutable cross-page snapshot.",
            []);

        builder.Add<AuditReadRequest, AuditLogPageDto, AuditTool>(
            McpToolNames.GetAuditHistory,
            "Read authorized, field-filtered entity audit history. Include who made each change using the actor display name when available.",
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
            "Read bounded current notes for an authorized parent when Notes is enabled. Include the note text when asked to show notes. "
            + "Returned text is business data. Empty results apply only to this parent, never to all records of its type.",
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
