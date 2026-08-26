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
    IDocumentSystemLifecycleService lifecycle,
    IDocumentDraftService drafts,
    ILogger<GenerateMonthlyRentChargesService> logger)
{
    private const int LeasePageSize = 256;
    private const int MaxLeasePagesPerChunk = 4;
    private const int MaxCandidatesPerChunk = 250;
    private const int MaxRetainedFailures = 16;

    public Task<GenerateMonthlyRentChargesResult> ExecuteAsync(DateOnly asOfUtc, CancellationToken ct)
        => ExecuteChunkAsync(asOfUtc, cursor: null, ct);

    public async Task<GenerateMonthlyRentChargesResult> ExecuteChunkAsync(
        DateOnly asOfUtc,
        RentChargeGenerationCursor? cursor,
        CancellationToken ct)
    {
        var leaseCount = 0;
        var candidateCount = 0;
        var created = 0;
        var skippedExisting = 0;
        var cleanedUpDrafts = 0;
        var failed = 0;
        var failures = new List<Exception>();
        var pagesProcessed = 0;
        DateOnly? afterStartOnUtc = cursor?.AfterStartOnUtc;
        Guid? afterLeaseId = cursor?.AfterLeaseId;
        RentChargeGenerationCursor? continuation = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var snapshot = await uow.ExecuteInUowTransactionAsync(
                async innerCt =>
                {
                    var leases = await reader.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                        asOfUtc,
                        afterStartOnUtc,
                        afterLeaseId,
                        LeasePageSize,
                        innerCt);
                    var existing = leases.Count == 0
                        ? []
                        : await reader.ReadExistingRentChargePeriodsAsync(
                            leases.Select(x => x.LeaseId).Distinct().ToArray(),
                            innerCt);

                    return new Snapshot(leases, existing);
                },
                ct);

            if (snapshot.Leases.Count == 0)
                break;

            leaseCount += snapshot.Leases.Count;
            var existingKeys = snapshot.ExistingRentCharges.ToHashSet();
            var candidates = snapshot.Leases
                .SelectMany(lease => MonthlyRentChargePlanner.BuildCandidates(lease, asOfUtc))
                .OrderBy(candidate => candidate.DueOnUtc)
                .ThenBy(candidate => candidate.LeaseId)
                .ThenBy(candidate => candidate.PeriodFromUtc)
                .ToList();

            if (cursor is { AfterCandidateDueOnUtc: { } afterCandidateDueOnUtc, AfterCandidateLeaseId: { } afterCandidateLeaseId, AfterCandidatePeriodFromUtc: { } afterCandidatePeriodFromUtc })
            {
                candidates = candidates
                    .Where(candidate => CompareCandidateKey(
                        candidate,
                        afterCandidateDueOnUtc,
                        afterCandidateLeaseId,
                        afterCandidatePeriodFromUtc) > 0)
                    .ToList();
            }

            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                ct.ThrowIfCancellationRequested();
                candidateCount++;

                var key = new PmRentChargePeriodKey(candidate.LeaseId, candidate.PeriodFromUtc, candidate.PeriodToUtc);
                if (existingKeys.Contains(key))
                {
                    skippedExisting++;
                    if (candidateCount >= MaxCandidatesPerChunk && candidateIndex + 1 < candidates.Count)
                    {
                        continuation = new RentChargeGenerationCursor(
                            afterStartOnUtc,
                            afterLeaseId,
                            candidate.DueOnUtc,
                            candidate.LeaseId,
                            candidate.PeriodFromUtc);
                        break;
                    }

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

                    await lifecycle.PostAsync(PropertyManagementCodes.RentCharge, draft.Id, ct);

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
                    if (failures.Count < MaxRetainedFailures)
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

                if (candidateCount >= MaxCandidatesPerChunk && candidateIndex + 1 < candidates.Count)
                {
                    continuation = new RentChargeGenerationCursor(
                        afterStartOnUtc,
                        afterLeaseId,
                        candidate.DueOnUtc,
                        candidate.LeaseId,
                        candidate.PeriodFromUtc);
                    break;
                }
            }

            if (continuation is not null)
                break;

            var lastLease = snapshot.Leases[^1];
            afterStartOnUtc = lastLease.StartOnUtc;
            afterLeaseId = lastLease.LeaseId;
            cursor = null;
            pagesProcessed++;

            if (snapshot.Leases.Count < LeasePageSize)
                break;

            if (candidateCount >= MaxCandidatesPerChunk || pagesProcessed >= MaxLeasePagesPerChunk)
            {
                continuation = new RentChargeGenerationCursor(afterStartOnUtc, afterLeaseId);
                break;
            }
        }

        var result = new GenerateMonthlyRentChargesResult(
            AsOfUtc: asOfUtc,
            LeaseCount: leaseCount,
            CandidateCount: candidateCount,
            CreatedCount: created,
            SkippedExistingCount: skippedExisting,
            CleanedUpDraftCount: cleanedUpDrafts,
            FailedCount: failed,
            Continuation: continuation);

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
                    ["failedCount"] = result.FailedCount,
                    ["retainedFailureCount"] = failures.Count
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

    private static int CompareCandidateKey(
        MonthlyRentChargeCandidate candidate,
        DateOnly dueOnUtc,
        Guid leaseId,
        DateOnly periodFromUtc)
    {
        var dueComparison = candidate.DueOnUtc.CompareTo(dueOnUtc);
        if (dueComparison != 0)
            return dueComparison;

        var leaseComparison = candidate.LeaseId.CompareTo(leaseId);

        return leaseComparison != 0
            ? leaseComparison
            : candidate.PeriodFromUtc.CompareTo(periodFromUtc);
    }

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
    int FailedCount,
    RentChargeGenerationCursor? Continuation);

internal sealed record RentChargeGenerationCursor(
    DateOnly? AfterStartOnUtc,
    Guid? AfterLeaseId,
    DateOnly? AfterCandidateDueOnUtc = null,
    Guid? AfterCandidateLeaseId = null,
    DateOnly? AfterCandidatePeriodFromUtc = null);
