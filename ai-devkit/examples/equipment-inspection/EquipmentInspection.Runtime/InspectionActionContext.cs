using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Core.Security;

namespace EquipmentInspection.Runtime;

public sealed class InspectionActionContext(IInspectionReader reader) : IDocumentActionContextEnricher
{
    public string DocumentTypeCode => EquipmentDefinitions.InspectionCode;

    public async Task<IReadOnlyDictionary<string, object?>> LoadFactsAsync(DocumentActionContextRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!request.Security.Has(NgbResourceKinds.Document, DocumentTypeCode, NgbPermissionActions.View))
            return new Dictionary<string, object?>();

        var inspection = await reader.ReadAsync(request.Document.Id, ct);
        return new Dictionary<string, object?>
        {
            [InspectionDeferralDefinitions.EquipmentSelectedFact] = inspection?.EquipmentId is { } equipment && equipment != Guid.Empty
        };
    }
}
