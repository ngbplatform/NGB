using Dapper;
using NGB.Contracts.Metadata;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.BackgroundJobs;

namespace NGB.PropertyManagement.PostgreSql.BackgroundJobs;

public sealed class PropertyManagementRentChargeGenerationReader(IUnitOfWork uow)
    : IPropertyManagementRentChargeGenerationReader
{
    public async Task<IReadOnlyList<PmRentChargeGenerationLease>> ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
        DateOnly asOfUtc,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    l.document_id AS LeaseId,
    l.start_on_utc AS StartOnUtc,
    l.end_on_utc AS EndOnUtc,
    l.rent_amount AS RentAmount,
    l.due_day AS DueDay
FROM documents d
JOIN doc_pm_lease l
  ON l.document_id = d.id
WHERE d.status = @posted
  AND l.start_on_utc <= @as_of_utc
ORDER BY l.start_on_utc, l.document_id;
""";

        var rows = await uow.Connection.QueryAsync<PmRentChargeGenerationLease>(
            new CommandDefinition(
                sql,
                new
                {
                    posted = (int)DocumentStatus.Posted,
                    as_of_utc = asOfUtc
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<PmRentChargePeriodKey>> ReadExistingRentChargePeriodsAsync(
        IReadOnlyCollection<Guid> leaseIds,
        CancellationToken ct = default)
    {
        if (leaseIds.Count == 0)
            return [];

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rc.lease_id AS LeaseId,
    rc.period_from_utc AS PeriodFromUtc,
    rc.period_to_utc AS PeriodToUtc
FROM documents d
JOIN doc_pm_rent_charge rc
  ON rc.document_id = d.id
WHERE d.status <> @marked_for_deletion
  AND rc.lease_id = ANY(@lease_ids)
ORDER BY rc.lease_id, rc.period_from_utc, rc.period_to_utc;
""";

        var rows = await uow.Connection.QueryAsync<PmRentChargePeriodKey>(
            new CommandDefinition(
                sql,
                new
                {
                    marked_for_deletion = (int)DocumentStatus.MarkedForDeletion,
                    lease_ids = leaseIds.Distinct().ToArray()
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToList();
    }
}
