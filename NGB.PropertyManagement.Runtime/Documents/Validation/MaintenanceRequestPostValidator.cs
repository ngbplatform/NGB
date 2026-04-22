using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class MaintenanceRequestPostValidator(
    IPropertyManagementDocumentReaders readers,
    ICatalogRepository catalogRepository,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.MaintenanceRequest;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(MaintenanceRequestPostValidator));

        var request = await readers.ReadMaintenanceRequestHeadAsync(documentForUpdate.Id, ct);

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw MaintenanceRequestValidationException.SubjectRequired(documentForUpdate.Id);

        MaintenanceRequestValidationGuards.NormalizePriorityOrThrow(request.Priority, documentForUpdate.Id);
        await MaintenanceRequestValidationGuards.ValidatePropertyAsync(request.PropertyId, documentForUpdate.Id, readers, ct);
        await MaintenanceRequestValidationGuards.ValidatePartyAsync(request.PartyId, documentForUpdate.Id, parties, ct);
        await MaintenanceRequestValidationGuards.ValidateCategoryAsync(request.CategoryId, documentForUpdate.Id, catalogRepository, ct);
    }
}
