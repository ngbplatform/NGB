using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntrySystemReversalRunner : IGeneralJournalEntrySystemReversalRunner
{
    private const int MaxCandidatesScanMultiplier = 5;

    private readonly IGeneralJournalEntryRepository _gje;
    private readonly IGeneralJournalEntryDocumentService _service;
    private readonly ILogger<GeneralJournalEntrySystemReversalRunner> _logger;
    private readonly IGeneralJournalEntrySystemReversalBatchProcessor? _batchProcessor;

    public GeneralJournalEntrySystemReversalRunner(
        IGeneralJournalEntryRepository gje,
        IGeneralJournalEntryDocumentService service,
        ILogger<GeneralJournalEntrySystemReversalRunner> logger)
        : this(gje, service, logger, batchProcessor: null)
    {
    }

    internal GeneralJournalEntrySystemReversalRunner(
        IGeneralJournalEntryRepository gje,
        IGeneralJournalEntryDocumentService service,
        ILogger<GeneralJournalEntrySystemReversalRunner> logger,
        IGeneralJournalEntrySystemReversalBatchProcessor? batchProcessor)
    {
        _gje = gje;
        _service = service;
        _logger = logger;
        _batchProcessor = batchProcessor;
    }

    public async Task<int> PostDueSystemReversalsAsync(
        DateOnly utcDate,
        int batchSize,
        string postedBy = "SYSTEM",
        CancellationToken ct = default)
    {
        if (batchSize <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(batchSize), batchSize, "Argument is out of range.");

        var maxCandidatesToScan = checked(batchSize * MaxCandidatesScanMultiplier);
        var scanned = 0;
        var posted = 0;
        DateTime? afterDateUtc = null;
        Guid? afterDocumentId = null;

        while (posted < batchSize && scanned < maxCandidatesToScan)
        {
            var remainingPosts = batchSize - posted;
            var remainingScanBudget = maxCandidatesToScan - scanned;
            var pageSize = Math.Min(remainingPosts, remainingScanBudget);
            var candidates = await _gje.GetDueSystemReversalCandidatesAsync(
                utcDate,
                pageSize,
                afterDateUtc,
                afterDocumentId,
                ct);

            if (candidates.Count == 0)
                break;

            // Do not rely on a persistence implementation to honor the requested limit:
            // the public batch-size and scan-budget contracts remain hard upper bounds.
            var page = candidates.Count <= pageSize
                ? candidates
                : candidates.Take(pageSize).ToArray();
            ct.ThrowIfCancellationRequested();
            afterDateUtc = page[^1].DateUtc;
            afterDocumentId = page[^1].DocumentId;
            scanned += page.Count;

            if (_batchProcessor is not null)
            {
                var results = await _batchProcessor.ProcessAsync(page, postedBy, ct);
                foreach (var result in results)
                {
                    if (result.Error is null)
                        posted++;
                    else
                        _logger.LogWarning(result.Error, "Failed to post due system reversal {DocumentId}.", result.DocumentId);
                }
            }
            else
            {
                foreach (var candidate in page)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        // NOTE: service.PostApprovedAsync will lock and ensure idempotency.
                        await _service.PostApprovedAsync(candidate.DocumentId, postedBy, ct);
                        posted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to post due system reversal {DocumentId}.", candidate.DocumentId);
                    }
                }
            }

            if (page.Count < pageSize)
                break;
        }

        return posted;
    }
}

internal readonly record struct GeneralJournalEntrySystemReversalPostResult(Guid DocumentId, Exception? Error);

internal interface IGeneralJournalEntrySystemReversalBatchProcessor
{
    Task<IReadOnlyList<GeneralJournalEntrySystemReversalPostResult>> ProcessAsync(
        IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate> candidates,
        string postedBy,
        CancellationToken ct);
}

internal sealed class GeneralJournalEntrySystemReversalBatchProcessor(IServiceScopeFactory scopes)
    : IGeneralJournalEntrySystemReversalBatchProcessor
{
    private const int MaxDegreeOfParallelism = 4;

    public async Task<IReadOnlyList<GeneralJournalEntrySystemReversalPostResult>> ProcessAsync(
        IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate> candidates,
        string postedBy,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return [];

        var results = new GeneralJournalEntrySystemReversalPostResult[candidates.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Min(MaxDegreeOfParallelism, candidates.Count)
            },
            async (index, innerCt) =>
            {
                var candidate = candidates[index];
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
                    await service.PostApprovedAsync(candidate.DocumentId, postedBy, innerCt);
                    results[index] = new GeneralJournalEntrySystemReversalPostResult(candidate.DocumentId, Error: null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !innerCt.IsCancellationRequested)
                {
                    results[index] = new GeneralJournalEntrySystemReversalPostResult(candidate.DocumentId, ex);
                }
            });

        return results;
    }
}
