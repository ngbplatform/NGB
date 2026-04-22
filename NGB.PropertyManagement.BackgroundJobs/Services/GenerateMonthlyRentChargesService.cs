using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.PropertyManagement.BackgroundJobs.Catalog;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using NGB.Persistence.UnitOfWork;
using NGB.Tools;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.BackgroundJobs.Services;

internal sealed class GenerateMonthlyRentChargesService(
    IUnitOfWork uow,
    IPropertyManagementRentChargeGenerationReader reader,
    IDocumentService documents,
    IDocumentDraftService drafts,
    ILogger<GenerateMonthlyRentChargesService> logger)
{
    public async Task<GenerateMonthlyRentChargesResult> ExecuteAsync(DateOnly asOfUtc, CancellationToken ct)
    {
        var snapshot = await uow.ExecuteInUowTransactionAsync(
            async innerCt =>
            {
                var leases = await reader.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(asOfUtc, innerCt);
                var existing = leases.Count == 0
                    ? []
                    : await reader.ReadExistingRentChargePeriodsAsync(leases.Select(x => x.LeaseId).Distinct().ToArray(), innerCt);

                return new Snapshot(leases, existing);
            },
            ct);

        var existingKeys = snapshot.ExistingRentCharges.ToHashSet();
        var candidates = snapshot.Leases
            .SelectMany(lease => MonthlyRentChargePlanner.BuildCandidates(lease, asOfUtc))
            .OrderBy(candidate => candidate.DueOnUtc)
            .ThenBy(candidate => candidate.LeaseId)
            .ThenBy(candidate => candidate.PeriodFromUtc)
            .ToList();

        var created = 0;
        var skippedExisting = 0;
        var cleanedUpDrafts = 0;
        var failed = 0;
        var failures = new List<Exception>();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var key = new PmRentChargePeriodKey(candidate.LeaseId, candidate.PeriodFromUtc, candidate.PeriodToUtc);
            if (existingKeys.Contains(key))
            {
                skippedExisting++;
                continue;
            }

            DocumentDto? draft = null;

            try
            {
                draft = await documents.CreateDraftAsync(
                    PropertyManagementCodes.RentCharge,
                    BuildPayload(candidate),
                    ct);

                await drafts.UpdateDraftAsync(
                    draft.Id,
                    number: null,
                    dateUtc: ToDocumentDateUtc(candidate.DueOnUtc),
                    manageTransaction: true,
                    ct: ct);

                await documents.PostAsync(PropertyManagementCodes.RentCharge, draft.Id, ct);

                existingKeys.Add(key);
                created++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add(ex);

                logger.LogError(
                    ex,
                    "PM background job '{JobId}' failed for LeaseId='{LeaseId}' period {PeriodFromUtc:yyyy-MM-dd}..{PeriodToUtc:yyyy-MM-dd}.",
                    PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges,
                    candidate.LeaseId,
                    candidate.PeriodFromUtc,
                    candidate.PeriodToUtc);

                if (draft is not null)
                {
                    try
                    {
                        if (await drafts.DeleteDraftAsync(draft.Id, manageTransaction: true, ct))
                            cleanedUpDrafts++;
                    }
                    catch (Exception cleanupEx)
                    {
                        logger.LogWarning(
                            cleanupEx,
                            "PM background job '{JobId}' could not delete failed Draft Rent Charge '{DocumentId}'.",
                            PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges,
                            draft.Id);
                    }
                }
            }
        }

        var result = new GenerateMonthlyRentChargesResult(
            AsOfUtc: asOfUtc,
            LeaseCount: snapshot.Leases.Count,
            CandidateCount: candidates.Count,
            CreatedCount: created,
            SkippedExistingCount: skippedExisting,
            CleanedUpDraftCount: cleanedUpDrafts,
            FailedCount: failed);

        logger.LogInformation(
            "PM background job '{JobId}' completed. AsOfUtc={AsOfUtc:yyyy-MM-dd} LeaseCount={LeaseCount} CandidateCount={CandidateCount} CreatedCount={CreatedCount} SkippedExistingCount={SkippedExistingCount} CleanedUpDraftCount={CleanedUpDraftCount} FailedCount={FailedCount}.",
            PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges,
            result.AsOfUtc,
            result.LeaseCount,
            result.CandidateCount,
            result.CreatedCount,
            result.SkippedExistingCount,
            result.CleanedUpDraftCount,
            result.FailedCount);

        if (failures.Count > 0)
        {
            throw new NgbUnexpectedException(
                operation: "pm.backgroundjobs.generate_monthly_rent_charges",
                innerException: new AggregateException(failures),
                additionalContext: new Dictionary<string, object?>
                {
                    ["asOfUtc"] = result.AsOfUtc,
                    ["leaseCount"] = result.LeaseCount,
                    ["candidateCount"] = result.CandidateCount,
                    ["createdCount"] = result.CreatedCount,
                    ["skippedExistingCount"] = result.SkippedExistingCount,
                    ["cleanedUpDraftCount"] = result.CleanedUpDraftCount,
                    ["failedCount"] = result.FailedCount
                });
        }

        return result;
    }

    private static RecordPayload BuildPayload(MonthlyRentChargeCandidate candidate)
    {
        return new RecordPayload(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["lease_id"] = JsonTools.J(candidate.LeaseId),
                ["period_from_utc"] = JsonTools.J(candidate.PeriodFromUtc.ToString("yyyy-MM-dd")),
                ["period_to_utc"] = JsonTools.J(candidate.PeriodToUtc.ToString("yyyy-MM-dd")),
                ["due_on_utc"] = JsonTools.J(candidate.DueOnUtc.ToString("yyyy-MM-dd")),
                ["amount"] = JsonTools.J(candidate.Amount),
                ["memo"] = JsonTools.J(candidate.Memo)
            });
    }

    private static DateTime ToDocumentDateUtc(DateOnly date)
        => new(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Snapshot(
        IReadOnlyList<PmRentChargeGenerationLease> Leases,
        IReadOnlyList<PmRentChargePeriodKey> ExistingRentCharges);
}

internal sealed record GenerateMonthlyRentChargesResult(
    DateOnly AsOfUtc,
    int LeaseCount,
    int CandidateCount,
    int CreatedCount,
    int SkippedExistingCount,
    int CleanedUpDraftCount,
    int FailedCount);
