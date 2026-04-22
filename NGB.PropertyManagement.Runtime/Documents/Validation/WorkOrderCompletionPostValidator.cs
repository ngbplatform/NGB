using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class WorkOrderCompletionPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.WorkOrderCompletion;

    public async Task ValidateBeforePostAsync(NGB.Core.Documents.DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(WorkOrderCompletionPostValidator));

        var completion = await readers.ReadWorkOrderCompletionHeadAsync(documentForUpdate.Id, ct);

        WorkOrderCompletionValidationGuards.NormalizeOutcomeOrThrow(completion.Outcome, documentForUpdate.Id);

        await WorkOrderCompletionValidationGuards.ValidateWorkOrderAsync(
            completion.WorkOrderId,
            documentForUpdate.Id,
            documents,
            ct);

        await WorkOrderCompletionValidationGuards.EnsureNoOtherPostedCompletionAsync(
            completion.WorkOrderId,
            documentForUpdate.Id,
            documentForUpdate.Id,
            readers,
            ct);
    }
}
