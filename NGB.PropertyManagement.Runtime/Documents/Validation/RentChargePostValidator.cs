using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// Posting-time safety net for pm.rent_charge.
///
/// Why both draft-time and post-time checks exist:
/// - draft validation gives the user immediate feedback on Save;
/// - post validation re-checks current state because the referenced lease may change
///   (or be deleted/marked) after the draft was created.
/// </summary>
public sealed class RentChargePostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.RentCharge;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(RentChargePostValidator));

        var rent = await readers.ReadRentChargeHeadAsync(documentForUpdate.Id, ct);

        var leaseDocument = await documents.GetAsync(rent.LeaseId, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw RentChargeValidationException.LeaseNotFound(rent.LeaseId);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw RentChargeValidationException.LeaseMarkedForDeletion(rent.LeaseId);

        var lease = await readers.ReadLeaseHeadAsync(rent.LeaseId, ct);
        await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, "lease_id", lease.PrimaryPartyId, parties, ct);

        if (rent.Amount <= 0m)
            throw RentChargeValidationException.AmountMustBePositive(rent.Amount);

        if (rent.PeriodFromUtc > rent.PeriodToUtc)
            throw RentChargeValidationException.PeriodRangeInvalid(rent.PeriodFromUtc, rent.PeriodToUtc);

        if (rent.PeriodFromUtc < lease.StartOnUtc)
            throw RentChargeValidationException.PeriodOutsideLeaseTerm(
                rent.PeriodFromUtc,
                rent.PeriodToUtc,
                lease.StartOnUtc,
                lease.EndOnUtc);

        if (lease.EndOnUtc is not null && rent.PeriodToUtc > lease.EndOnUtc.Value)
            throw RentChargeValidationException.PeriodOutsideLeaseTerm(
                rent.PeriodFromUtc,
                rent.PeriodToUtc,
                lease.StartOnUtc,
                lease.EndOnUtc);
    }
}
