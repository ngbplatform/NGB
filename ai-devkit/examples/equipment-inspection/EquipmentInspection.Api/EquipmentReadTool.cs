using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Mcp;
using NGB.Mcp.Tools;

namespace EquipmentInspection.Api;

public sealed record EquipmentReadRequest(Guid Id);

public sealed record EquipmentReadResult(Guid Id, string Name);

public sealed class EquipmentReadTool(IAuthorizedCatalogReadService catalogs)
    : IMcpReadToolHandler<EquipmentReadRequest, EquipmentReadResult>,
        IMcpReadSourceProvider<EquipmentReadRequest, EquipmentReadResult>
{
    public async Task<EquipmentReadResult> ExecuteAsync(EquipmentReadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Id == Guid.Empty)
            throw new McpReadException("invalid_input", "A saved equipment ID is required.");

        var equipment = await catalogs.GetByIdAsync(EquipmentDefinitions.EquipmentCode, request.Id, ct);
        return new(equipment.Id, equipment.Display!);
    }

    public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        EquipmentReadRequest request, EquipmentReadResult result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<McpReadSource> sources =
        [
            new(McpSourceKind.Catalog, EquipmentDefinitions.EquipmentCode, result.Id, result.Name)
        ];
        return ValueTask.FromResult(sources);
    }
}
