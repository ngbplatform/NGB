using Microsoft.Extensions.Logging.Abstractions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Runtime.Documents;

namespace NGB.PropertyManagement.BackgroundJobs.Services;

internal sealed record RentChargeCandidateExecutionResult(
    MonthlyRentChargeCandidate Candidate,
    bool Created,
    bool CleanedUpDraft,
    Exception? Error);

internal interface IRentChargeCandidateBatchExecutor
{
    Task<IReadOnlyList<RentChargeCandidateExecutionResult>> ExecuteAsync(
        IReadOnlyList<MonthlyRentChargeCandidate> candidates,
        CancellationToken ct);
}

/// <summary>
/// Executes independent rent-charge lifecycles concurrently, but gives every candidate its own
/// dependency-injection scope and UnitOfWork. This avoids unsafe concurrent use of scoped Npgsql
/// connections while retaining per-document transactions and failure isolation.
/// </summary>
internal sealed class ScopedRentChargeCandidateBatchExecutor(IServiceScopeFactory scopeFactory)
    : IRentChargeCandidateBatchExecutor
{
    private const int MaxDegreeOfParallelism = 4;

    public async Task<IReadOnlyList<RentChargeCandidateExecutionResult>> ExecuteAsync(
        IReadOnlyList<MonthlyRentChargeCandidate> candidates,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return [];

        var results = new RentChargeCandidateExecutionResult[candidates.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = MaxDegreeOfParallelism
            },
            async (index, innerCt) =>
            {
                var scope = scopeFactory.CreateAsyncScope();
                try
                {
                    var worker = scope.ServiceProvider.GetRequiredService<RentChargeCandidateWorker>();
                    results[index] = await worker.ExecuteAsync(candidates[index], innerCt);
                }
                finally
                {
                    await scope.DisposeAsync();
                }
            });

        return results;
    }
}

internal sealed class SequentialRentChargeCandidateBatchExecutor(RentChargeCandidateWorker worker)
    : IRentChargeCandidateBatchExecutor
{
    public async Task<IReadOnlyList<RentChargeCandidateExecutionResult>> ExecuteAsync(
        IReadOnlyList<MonthlyRentChargeCandidate> candidates,
        CancellationToken ct)
    {
        var results = new RentChargeCandidateExecutionResult[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            results[index] = await worker.ExecuteAsync(candidates[index], ct);
        }

        return results;
    }
}

internal sealed class RentChargeCandidateWorker(
    IDocumentService documents,
    IDocumentSystemLifecycleService lifecycle,
    IDocumentDraftService drafts,
    ILogger<RentChargeCandidateWorker> logger)
{
    public async Task<RentChargeCandidateExecutionResult> ExecuteAsync(
        MonthlyRentChargeCandidate candidate,
        CancellationToken ct)
    {
        DocumentDto? draft = null;
        try
        {
            draft = await documents.CreateDraftAsync(
                PropertyManagementCodes.RentCharge,
                GenerateMonthlyRentChargesService.BuildPayload(candidate),
                ct);

            await drafts.UpdateDraftAsync(
                draft.Id,
                number: null,
                dateUtc: GenerateMonthlyRentChargesService.ToDocumentDateUtc(candidate.DueOnUtc),
                manageTransaction: true,
                ct: ct);

            await lifecycle.PostAsync(PropertyManagementCodes.RentCharge, draft.Id, ct);

            return new RentChargeCandidateExecutionResult(candidate, Created: true, CleanedUpDraft: false, Error: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var cleanedUp = false;
            if (draft is not null)
            {
                try
                {
                    cleanedUp = await drafts.DeleteDraftAsync(draft.Id, manageTransaction: true, ct);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(
                        cleanupEx,
                        "Could not delete failed Draft Rent Charge '{DocumentId}'.",
                        draft.Id);
                }
            }

            return new RentChargeCandidateExecutionResult(candidate, Created: false, cleanedUp, ex);
        }
    }

    public static RentChargeCandidateWorker CreateSequential(
        IDocumentService documents,
        IDocumentSystemLifecycleService lifecycle,
        IDocumentDraftService drafts)
        => new(documents, lifecycle, drafts, NullLogger<RentChargeCandidateWorker>.Instance);
}
