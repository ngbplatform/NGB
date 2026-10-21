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
            "Discover authorized catalog, document and report types with bounded paging. "
            + "These are type definitions, not stored records or record counts. Use this to find a type by its business name.",
            []);

        builder.Add<MetadataRequest, MetadataResult, GetMetadataTool>(
            McpToolNames.GetMetadata,
            "Read a permitted canonical definition using a type code returned by discovery, search or page context. "
            + "Metadata does not authorize mutations.",
            []);

        builder.Add<SearchRequest, SearchResult, SearchTool>(
            McpToolNames.Search,
            "Find authorized records and reports by a supplied document number, name or display text. "
            + "Use metadata discovery for generic type names; for example, a request for invoices does not supply an invoice name. "
            + "Search cannot select records by dates, amounts or calculated business states. "
            + "Scope by kind and typeCode only when known from metadata or context; otherwise omit them. Retry without scope if a scoped search is empty. "
            + "Results provide real record IDs and display titles; truncated includes capped type fan-out. "
            + "Search discovers matching records; the number of hits is not an aggregate business count.",
            []);

        builder.Add<RecordsRequest, RecordsResult, ListRecordsTool>(
            McpToolNames.ListRecords,
            "Read a page of records using the type's declared server-side selection. Read its metadata first. "
            + "Call only when declared filters cover every requested field criterion, or the user asks for names, an unfiltered list or examples. "
            + "If metadata has a date field but no filter for the requested date selection, this operation cannot answer that question. "
            + "Discover a suitable report or explain the missing selection capability; do not call this to inspect sample rows. "
            + "Search matches supplied names or numbers. Filters map declared codes directly to strings, not value objects. "
            + "For exact record counts, use includeTotal=true and limit=1; total covers all matches, including later pages. "
            + "A page is not the complete population while a continuation is present. No arbitrary sorting.",
            []);

        builder.Add<RecordRequest, RecordResult, GetRecordTool>(
            McpToolNames.GetRecord,
            "Read details of an authorized catalog item, document or general journal entry using a record UUID returned by search or a record list. "
            + "Document numbers and display names are not record IDs. Search for them first.",
            []);

        builder.Add<FlowRequest, FlowResult, FlowTool>(
            McpToolNames.GetDocumentFlow,
            "Read a bounded graph containing only permitted, reachable objects and their declared relationships. "
            + "Read related details when needed to establish the requested business fact; a relationship alone does not establish an outcome. "
            + "An incomplete graph cannot prove that no related objects exist.",
            []);

        builder.Add<EffectsRequest, EffectsResult, EffectsTool>(
            McpToolNames.GetDocumentEffects,
            "Read reconstructed current effects for a document. Full-limit completeness is unknown; IDs are result-local, not journal history.",
            []);

        builder.Add<ReportsRequest, ReportsResult, ListReportsTool>(
            McpToolNames.ListReports,
            "Discover installed reports: authorized names and descriptions with paging. Start here for business measures, totals and balances, "
            + "including when the current page is a catalog or document. Select a report whose description covers the requested measure and scope. "
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
            + "All request.parameters values are strings, including booleans and numbers. Date parameters use yyyy-MM-dd. "
            + "Omit optional date parameters when no period was requested. Never extract a date from a record name or identifier. "
            + "Expand a returned group by copying its childrenPath to request.groupPath, keeping the same filters and parameters and resetting the cursor. "
            + "This works independently of configurable layout capabilities. Reuses existing calculations and pagination; no export or immutable cross-page snapshot.",
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
