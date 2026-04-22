using Microsoft.Extensions.Logging;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntrySystemReversalRunner(
    IGeneralJournalEntryRepository gje,
    IGeneralJournalEntryDocumentService service,
    ILogger<GeneralJournalEntrySystemReversalRunner> logger)
    : IGeneralJournalEntrySystemReversalRunner
{
    private const int MaxCandidatesScanMultiplier = 5;

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
            var candidates = await gje.GetDueSystemReversalCandidatesAsync(
                utcDate,
                pageSize,
                afterDateUtc,
                afterDocumentId,
                ct);

            if (candidates.Count == 0)
                break;

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();

                afterDateUtc = candidate.DateUtc;
                afterDocumentId = candidate.DocumentId;
                scanned++;

                try
                {
                    // NOTE: service.PostApprovedAsync will lock and ensure idempotency.
                    await service.PostApprovedAsync(candidate.DocumentId, postedBy, ct);
                    posted++;

                    if (posted >= batchSize || scanned >= maxCandidatesToScan)
                        break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to post due system reversal {DocumentId}.", candidate.DocumentId);
                }
            }

            if (candidates.Count < pageSize)
                break;
        }

        return posted;
    }
}
