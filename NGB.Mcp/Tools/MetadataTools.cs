using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;

namespace NGB.Mcp.Tools;

internal sealed class MetadataReader(
    IAuthorizedCatalogReadService catalogs,
    IAuthorizedDocumentReadService documents,
    IAuthorizedReportReadService reports)
{
    internal async Task<IReadOnlyList<MetadataSummary>> ListAsync(McpSourceKind? kind, CancellationToken ct)
    {
        var items = new List<MetadataSummary>();

        if (kind is null or McpSourceKind.Catalog)
        {
            var metadata = await catalogs.GetAllMetadataAsync(ct);
            items.AddRange(metadata.Select(item => new MetadataSummary(McpSourceKind.Catalog, item.CatalogType, item.DisplayName)));
        }

        if (kind is null or McpSourceKind.Document or McpSourceKind.GeneralJournalEntry)
        {
            var metadata = await documents.GetAllMetadataAsync(ct);
            items.AddRange(metadata.Select(item => new MetadataSummary(
                    item.DocumentType == AccountingDocumentTypeCodes.GeneralJournalEntry
                        ? McpSourceKind.GeneralJournalEntry
                        : McpSourceKind.Document,
                    item.DocumentType, item.DisplayName))
                .Where(item => kind is null || item.Kind == kind));
        }

        if (kind is null or McpSourceKind.Report)
        {
            var metadata = await reports.GetAllDefinitionsAsync(ct);
            items.AddRange(metadata.Select(item => new MetadataSummary(McpSourceKind.Report, item.ReportCode, item.Name)));
        }

        return items
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.TypeCode, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed class ListMetadataTool(MetadataReader reader)
    : IMcpReadToolHandler<MetadataListRequest, MetadataPage>, IMcpReadSourceProvider<MetadataListRequest, MetadataPage>
{
    public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        MetadataListRequest request,
        MetadataPage result,
        CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<McpReadSource>>(result.Items
            .Where(item => item.Kind == McpSourceKind.Report)
            .Select(item => new McpReadSource(item.Kind, item.TypeCode, null, item.Name))
            .ToArray());

    public async Task<MetadataPage> ExecuteAsync(MetadataListRequest request, CancellationToken ct)
    {
        var items = await reader.ListAsync(request.Kind, ct);

        return new MetadataPage(
            items
                .Skip(request.Offset)
                .Take(request.Limit)
                .ToArray(),
            request.Offset,
            request.Limit,
            items.Count > request.Offset + request.Limit);
    }
}

internal sealed class GetMetadataTool(
    IAuthorizedCatalogReadService catalogs,
    IAuthorizedDocumentReadService documents,
    IAuthorizedReportReadService reports)
    : IMcpReadToolHandler<MetadataRequest, MetadataResult>, IMcpReadSourceProvider<MetadataRequest, MetadataResult>
{
    public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        MetadataRequest request,
        MetadataResult result,
        CancellationToken ct)
        => ValueTask.FromResult(ReadSources.Report(result.Report));

    public async Task<MetadataResult> ExecuteAsync(MetadataRequest request, CancellationToken ct)
    {
        ReadValidation.RequireRecordKind(request.Kind, request.TypeCode);

        return request.Kind switch
        {
            McpSourceKind.Catalog => new MetadataResult(Catalog: (await catalogs.GetTypeMetadataAsync(request.TypeCode, ct)) with { Capabilities = null }),
            McpSourceKind.Document or McpSourceKind.GeneralJournalEntry => new MetadataResult(Document: StripActions(await documents.GetTypeMetadataAsync(request.TypeCode, ct))),
            McpSourceKind.Report => new MetadataResult(Report: await reports.GetDefinitionAsync(request.TypeCode, ct)),
            _ => throw new McpReadException("invalid_input", "Use the canonical catalog, document or report metadata kind.")
        };
    }

    private static DocumentTypeMetadataDto StripActions(DocumentTypeMetadataDto metadata)
        => metadata with { Actions = [], Capabilities = null };
}

internal sealed class CapabilitiesTool(MetadataReader reader, McpReadDispatcher dispatcher)
    : IMcpReadToolHandler<EmptyReadRequest, CapabilitiesResult>
{
    public async Task<CapabilitiesResult> ExecuteAsync(EmptyReadRequest request, CancellationToken ct)
    {
        var definitions = await reader.ListAsync(null, ct);
        var categories = definitions
            .Select(item => item.Kind.ToCode())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tools = await dispatcher.ListAsync(ct);

        return new CapabilitiesResult(McpVersion.Value, true, categories, tools.Select(tool => tool.Name).ToArray());
    }
}
