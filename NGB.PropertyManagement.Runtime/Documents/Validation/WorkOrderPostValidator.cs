using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class WorkOrderPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    ICatalogRepository catalogRepository)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.WorkOrder;

    public async Task ValidateBeforePostAsync(NGB.Core.Documents.DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(WorkOrderPostValidator));

        var workOrder = await readers.ReadWorkOrderHeadAsync(documentForUpdate.Id, ct);

        WorkOrderValidationGuards.NormalizeCostResponsibilityOrThrow(workOrder.CostResponsibility, documentForUpdate.Id);
        await WorkOrderValidationGuards.ValidateRequestAsync(workOrder.RequestId, documentForUpdate.Id, documents, ct);

        if (workOrder.AssignedPartyId is not null)
            await WorkOrderValidationGuards.ValidateAssignedPartyAsync(workOrder.AssignedPartyId.Value, documentForUpdate.Id, catalogRepository, ct);
    }
}
