using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Core.Security;

namespace EquipmentInspection.Runtime;

public sealed class InspectionDeferralAuthorization : IDocumentActionAuthorizationEvaluator
{
    public ValueTask<DocumentActionAuthorizationResult> EvaluateAsync(DocumentActionEvaluationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var allowed = context.Security.Has(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode, NgbPermissionActions.View)
            && context.Security.Has(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode, NgbPermissionActions.EditDraft);
        return ValueTask.FromResult(allowed ? DocumentActionAuthorizationResult.Authorized : DocumentActionAuthorizationResult.Denied);
    }
}
