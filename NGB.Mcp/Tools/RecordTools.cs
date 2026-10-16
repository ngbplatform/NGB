using Microsoft.Extensions.Options;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;

namespace NGB.Mcp.Tools;

internal sealed class GetRecordTool(
    IAuthorizedCatalogReadService catalogs,
    IAuthorizedDocumentReadService documents,
    IAuthorizedGeneralJournalEntryReadService journal,
    IRecordReadScopeFactory readScopes,
    IOptions<McpReadOptions> options,
    IMcpNgbLinkResolver links)
    : IMcpReadToolHandler<RecordRequest, RecordResult>
{
    public async Task<RecordResult> ExecuteAsync(RecordRequest request, CancellationToken ct)
    {
        ReadValidation.RequireId(request.Id);
        ReadValidation.RequireRecordKind(request.Kind, request.TypeCode);

        if (request.Kind == McpSourceKind.Catalog)
            await catalogs.GetTypeMetadataAsync(request.TypeCode, ct);
        else if (request.Kind is McpSourceKind.Document or McpSourceKind.GeneralJournalEntry)
            await documents.GetTypeMetadataAsync(request.TypeCode, ct);
        else
            throw new McpReadException("invalid_input", "The requested kind is not a record.");

        var target = new BusinessObjectRef(request.Kind switch
            {
                McpSourceKind.Catalog => BusinessObjectKind.CatalogItem,
                McpSourceKind.GeneralJournalEntry => BusinessObjectKind.GeneralJournalEntry,
                _ => BusinessObjectKind.Document
            },
            request.TypeCode,
            request.Id);

        RecordResult result;
        try
        {
            await using var scope = await readScopes.OpenAsync(target, options.Value.MaxRecordRows, options.Value.MaxRecordBytes, ct);
            result = request.Kind switch
            {
                McpSourceKind.Catalog => new RecordResult(Catalog: await catalogs.GetByIdAsync(request.TypeCode, request.Id, ct)),
                McpSourceKind.GeneralJournalEntry => new RecordResult(GeneralJournalEntry: await journal.GetByIdAsync(request.Id, ct)),
                _ => new RecordResult(Document: await documents.GetByIdAsync(request.TypeCode, request.Id, ct))
            };
        }
        catch (RecordReadLimitExceededException)
        {
            throw new McpReadException("result_too_large", "The record exceeds detail-read limits. Use a paginated report or record list.");
        }

        return result with { Source = ReadValidation.Source(request.Kind, request.TypeCode, request.Id, links) };
    }
}

internal sealed class ListRecordsTool(
    IAuthorizedCatalogReadService catalogs,
    IAuthorizedDocumentReadService documents,
    IAuthorizedGeneralJournalEntryReadService journal)
    : IMcpReadToolHandler<RecordsRequest, RecordsResult>
{
    public async Task<RecordsResult> ExecuteAsync(RecordsRequest request, CancellationToken ct)
    {
        ReadValidation.RequireRecordKind(request.Kind, request.TypeCode);

        var page = new PageRequestDto(
            Limit: request.Limit,
            Search: request.Search,
            Filters: request.Filters,
            Cursor: request.Cursor,
            IncludeTotal: false);

        return request.Kind switch
        {
            McpSourceKind.Catalog => new RecordsResult(Catalogs: await catalogs.GetPageAsync(request.TypeCode, page, ct)),
            McpSourceKind.Document => new RecordsResult(Documents: await documents.GetPageAsync(request.TypeCode, page, ct)),
            McpSourceKind.GeneralJournalEntry when request.Filters is null or { Count: 0 }
                => new RecordsResult(GeneralJournalEntries: await journal.GetPageAsync(
                    0, request.Limit, request.Search, null, null, null, request.Cursor, ct)),
            _ => throw new McpReadException("invalid_input", "The requested record kind or filters are unsupported.")
        };
    }
}

internal sealed class FlowTool(IAuthorizedDocumentReadService documents, IMcpNgbLinkResolver links)
    : IMcpReadToolHandler<FlowRequest, FlowResult>
{
    public async Task<FlowResult> ExecuteAsync(FlowRequest request, CancellationToken ct)
    {
        ReadValidation.RequireId(request.Id);
        var graph = await documents.GetRelationshipGraphAsync(request.TypeCode, request.Id, request.Depth, request.MaxNodes, ct);
        var nodeLinks = graph.Nodes.ToDictionary(
            node => node.NodeId,
            node => ReadValidation.Source(node.Kind == Contracts.Metadata.EntityKind.Catalog
                    ? McpSourceKind.Catalog
                    : McpSourceKind.Document,
                node.TypeCode,
                node.EntityId, links).OpenInNgb,
            StringComparer.Ordinal);

        return new FlowResult(
            graph,
            ReadValidation.Source(McpSourceKind.Document, request.TypeCode, request.Id, links),
            nodeLinks);
    }
}

internal sealed class EffectsTool(IAuthorizedDocumentReadService documents, IMcpNgbLinkResolver links)
    : IMcpReadToolHandler<EffectsRequest, EffectsResult>
{
    public async Task<EffectsResult> ExecuteAsync(EffectsRequest request, CancellationToken ct)
    {
        ReadValidation.RequireId(request.Id);

        var effects = await documents.GetEffectsAsync(request.TypeCode, request.Id, request.Limit, ct);
        var limitReached = effects.AccountingEntries.Count >= request.Limit
            || effects.OperationalRegisterMovements.Count >= request.Limit
            || effects.ReferenceRegisterWrites.Count >= request.Limit;

        return new EffectsResult(
            effects,
            request.Limit,
            limitReached ? "unknown_at_limit" : "complete",
            "result_local",
            ReadValidation.Source(McpSourceKind.Document, request.TypeCode, request.Id, links));
    }
}

internal static class ReadValidation
{
    internal static McpRecordSource Source(McpSourceKind kind, string typeCode, Guid id, IMcpNgbLinkResolver links)
    {
        if (kind == McpSourceKind.Document && typeCode == AccountingDocumentTypeCodes.GeneralJournalEntry)
            kind = McpSourceKind.GeneralJournalEntry;

        var code = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

        return new McpRecordSource(kind, typeCode, id, links.Resolve(code, typeCode, id)?.AbsoluteUri);
    }

    internal static void RequireRecordKind(McpSourceKind kind, string typeCode)
    {
        var isJournal = string.Equals(typeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal);
        if (kind == McpSourceKind.GeneralJournalEntry && !isJournal || kind == McpSourceKind.Document && isJournal)
            throw new McpReadException("invalid_input", "General journal entries require their canonical kind and type code.");
    }

    internal static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
            throw new McpReadException("invalid_input", "A non-empty canonical object ID is required.");
    }
}
