namespace NGB.PropertyManagement.BackgroundJobs;

/// <summary>
/// Read-model used by recurring jobs that generate monthly PM rent charges.
///
/// IMPORTANT:
/// - Implementations are expected to run inside an already active unit-of-work transaction.
/// - Returned leases must represent POSTED pm.lease documents only.
/// - Existing charge periods must include both Draft and Posted pm.rent_charge documents
///   (MarkedForDeletion should be excluded so jobs can self-heal after deletions).
/// </summary>
public interface IPropertyManagementRentChargeGenerationReader
{
    Task<IReadOnlyList<PmRentChargeGenerationLease>> ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
        DateOnly asOfUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmRentChargePeriodKey>> ReadExistingRentChargePeriodsAsync(
        IReadOnlyCollection<Guid> leaseIds,
        CancellationToken ct = default);
}

public sealed record PmRentChargeGenerationLease(
    Guid LeaseId,
    DateOnly StartOnUtc,
    DateOnly? EndOnUtc,
    decimal RentAmount,
    int? DueDay);

public sealed record PmRentChargePeriodKey(
    Guid LeaseId,
    DateOnly PeriodFromUtc,
    DateOnly PeriodToUtc);
