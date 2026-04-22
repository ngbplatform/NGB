using Microsoft.Extensions.Logging;
using NGB.Accounting.Periods;
using NGB.Accounting.Balances;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using NGB.Persistence.Periods;
using NGB.Persistence.Locks;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounting;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Posting;

/// <summary>
/// PostingEngine = document processing
///
/// IMPORTANT:
/// Sometimes posting must be atomic with other state changes (e.g. document status change).
/// Therefore, PostingEngine supports two modes:
///  1. manageTransaction=true  (default): starts/commits/rollbacks its own transaction
///  2. manageTransaction=false: assumes an external transaction is already active
///
/// Idempotency:
/// - Uses accounting_posting_state to make Post/Unpost/Repost retry-safe.
/// - Operation is identified by (document_id, PostingOperation).
/// </summary>
public sealed class PostingEngine(
    IAccountingPostingContextFactory contextFactory,
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks,
    IAccountingEntryWriter entryWriter,
    IAccountingTurnoverWriter turnoverWriter,
    IDimensionSetService dimensionSetService,
    IAccountingOperationalBalanceReader operationalBalanceReader,
    IClosedPeriodRepository closedPeriodRepository,
    IAccountingPostingValidator validator,
    IPostingStateRepository postingLog,
    ILogger<PostingEngine> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly AccountingTurnoverCalculator _turnoverCalculator = new();
    /// <summary>
    /// Preferred overload: postingAction receives CancellationToken.
    /// </summary>
    public async Task PostAsync(
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        CancellationToken ct = default)
        => _ = await PostAsync(PostingOperation.Post, postingAction, manageTransaction: true, ct);

    /// <summary>
    /// Preferred overload: postingAction receives CancellationToken.
    /// </summary>
    public async Task PostAsync(
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        bool manageTransaction,
        CancellationToken ct = default)
        => _ = await PostAsync(PostingOperation.Post, postingAction, manageTransaction, ct);

    /// <summary>
    /// Preferred overload: postingAction receives CancellationToken.
    /// </summary>
    public Task<PostingResult> PostAsync(
        PostingOperation operation,
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        CancellationToken ct = default)
        => PostAsync(operation, postingAction, manageTransaction: true, ct);

    public async Task<PostingResult> PostAsync(
        PostingOperation operation,
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        bool manageTransaction,
        CancellationToken ct = default)
    {
        if (postingAction is null)
            throw new NgbArgumentRequiredException(nameof(postingAction));

        // 1. Generate accounting entries (in-memory)
        var context = await contextFactory.CreateAsync(ct);

        await postingAction(context, ct);

        // PostingEngine is an accounting pipeline and expects at least one accounting entry.
        // If you need idempotency without register writes ("log-only" operations), use IPostingStateRepository directly.
        if (context.Entries.Count == 0)
            throw new NgbInvariantViolationException(
                message: "PostingEngine requires at least one accounting entry. Use IPostingStateRepository directly for state-only operations.",
                context: new Dictionary<string, object?>
                {
                    ["operation"] = operation.ToString()
                });

        var documentId = context.Entries[0].DocumentId;

        // 2. Transaction
        if (manageTransaction)
        {
            await uow.BeginTransactionAsync(ct);
        }
        else if (!uow.HasActiveTransaction)
        {
            throw new NgbArgumentInvalidException(
                nameof(manageTransaction),
                "manageTransaction=false requires an active transaction in the caller.");
        }

        try
        {
            // 2.5 Concurrency guard: lock the document id to prevent concurrent operations
            // (e.g. Unpost vs Repost race that could produce double-storno).
            // Must be inside the same transaction as register writes.
            await advisoryLocks.LockDocumentAsync(documentId, ct);

            // 3. Idempotency begin (must be inside the same transaction as writes)
            // DocumentId must be known to use accounting posting state.
            if (documentId == Guid.Empty)
            {
                throw new NgbArgumentOutOfRangeException(
                    nameof(documentId),
                    documentId,
                    "Posting action must set a non-empty DocumentId for all entries.");
            }

            var startedAtUtc = _timeProvider.GetUtcNowDateTime();
            var beginResult = await postingLog.TryBeginAsync(documentId, operation, startedAtUtc, ct);

            if (beginResult == PostingStateBeginResult.AlreadyCompleted)
            {
                if (manageTransaction)
                    await uow.CommitAsync(CancellationToken.None);

                logger.LogInformation("Posting {Operation} already completed (idempotent).", operation);

                return PostingResult.AlreadyCompleted; // idempotent success
            }

            if (beginResult == PostingStateBeginResult.InProgress)
            {
                // Another transaction already started posting for this (documentId, operation)
                throw new PostingAlreadyInProgressException(documentId, operation);
            }

            // 4. Validate invariants (fail-fast)
            validator.Validate(context.Entries);

            if (context.Entries.Count == 0)
                throw new NgbInvariantViolationException(
                    message: "Posting produced zero entries. This is not allowed.",
                    context: new Dictionary<string, object?>
                    {
                        ["documentId"] = documentId,
                        ["operation"] = operation.ToString()
                    });

            logger.LogInformation("Posting {Operation} started (entries={EntryCount}).", operation, context.Entries.Count);
            logger.LogDebug("Posting {Operation}: firstDocumentId={DocumentId}.", operation, documentId);

            // 4.5 Concurrency guard: lock all affected accounting periods (prevents ClosePeriod vs Posting races)
            await LockPeriodsAsync(context.Entries, ct);
            logger.LogDebug("Posting {Operation}: period locks acquired.", operation);

            // 5. Guard: closed periods are forbidden
            await EnsurePeriodsNotClosedAsync(operation, context.Entries, ct);

            // 5.5 Resolve & persist Dimension Set IDs for both sides (DimensionBag -> DimensionSetId).
            await ResolveDimensionSetIdsAsync(context.Entries, ct);

            // 5.6 Guard: NegativeBalancePolicy (operational enforcement)
            await EnsureNegativeBalancePolicyAsync(context.Entries, ct);

            // 6. Persist register
            await entryWriter.WriteAsync(context.Entries, ct);

            // 7. Calculate & persist turnovers (monthly)
            var turnovers = _turnoverCalculator.Calculate(context.Entries).ToList();
            await turnoverWriter.WriteAsync(turnovers, ct);

            // 8. Mark state completed (still inside the transaction)
            await postingLog.MarkCompletedAsync(documentId, operation, _timeProvider.GetUtcNowDateTime(), ct);

            if (manageTransaction)
                await uow.CommitAsync(CancellationToken.None);

            return PostingResult.Executed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Posting engine operation failed.");
            if (manageTransaction)
                await uow.RollbackAsync(CancellationToken.None);

            throw;
        }
    }

    private async Task ResolveDimensionSetIdsAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
            return;

        // Multiple entries often share the same analytical dimension bag (e.g., symmetric postings).
        // Cache by canonical string to avoid duplicate GetOrCreateIdAsync calls and DB round-trips.
        var cache = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            // Posting handlers may set DimensionSetId explicitly (e.g., when dimensions are stored out-of-band).
            // PostingEngine must not overwrite non-empty IDs.
            if (e.DebitDimensionSetId == Guid.Empty)
                e.DebitDimensionSetId = await GetOrCreateSetIdAsync(e.DebitDimensions, cache, ct);

            if (e.CreditDimensionSetId == Guid.Empty)
                e.CreditDimensionSetId = await GetOrCreateSetIdAsync(e.CreditDimensions, cache, ct);
        }
    }

    private async Task<Guid> GetOrCreateSetIdAsync(DimensionBag bag, Dictionary<string, Guid> cache, CancellationToken ct)
    {
        if (bag.IsEmpty)
            return Guid.Empty;

        var canonical = Canonical(bag);
        if (cache.TryGetValue(canonical, out var existing))
            return existing;

        var id = await dimensionSetService.GetOrCreateIdAsync(bag, ct);
        cache.Add(canonical, id);
        return id;
    }

    private static string Canonical(DimensionBag bag)
        => string.Join(';', bag.Items.Select(x => $"{x.DimensionId:N}={x.ValueId:N}"));


    private async Task LockPeriodsAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct)
    {
        var periods = entries
            .Select(e => AccountingPeriod.FromDateTime(e.Period))
            .Distinct()
            .OrderBy(p => p) // deterministic order => avoids deadlocks when multiple periods are involved
            .ToList();

        foreach (var p in periods)
        {
            await advisoryLocks.LockPeriodAsync(p, ct);
        }
    }


    private async Task EnsureNegativeBalancePolicyAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct)
    {
        // Operational enforcement:
        // base = latest closed balance (<= month) + current month turnovers (to-date).
        // Then we add current posting deltas and ensure projected balance does not go negative
        // for accounts with NegativeBalancePolicy Warn/Forbid.
        if (entries.Count == 0)
            return;

        // Posting validator currently enforces a single UTC day, but we keep this generic.
        var periods = entries
            .Select(e => AccountingPeriod.FromDateTime(e.Period))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        foreach (var period in periods)
        {
            var periodEntries = entries
                .Where(e => AccountingPeriod.FromDateTime(e.Period) == period)
                .ToList();

            var keys = new List<AccountingBalanceKey>(periodEntries.Count * 2);
            var accountsByKey = new Dictionary<AccountingBalanceKey, Account>();

            foreach (var e in periodEntries)
            {
                var dk = new AccountingBalanceKey(e.Debit.Id, e.DebitDimensionSetId);
                keys.Add(dk);
                accountsByKey.TryAdd(dk, e.Debit);

                var ck = new AccountingBalanceKey(e.Credit.Id, e.CreditDimensionSetId);
                keys.Add(ck);
                accountsByKey.TryAdd(ck, e.Credit);
            }

            keys = keys.Distinct().ToList();

            // Fast-path: if all involved keys are Allow, skip DB reads entirely.
            if (accountsByKey.Values.All(a => a.NegativeBalancePolicy == NegativeBalancePolicy.Allow))
                continue;

            var snapshots = await operationalBalanceReader.GetForKeysAsync(period, keys, ct);

            // Guard against accidental duplicates from the reader: ToDictionary would throw ArgumentException.
            // We want a strict NGB invariant violation with diagnostic context.
            var snapByKey = new Dictionary<AccountingBalanceKey, AccountingOperationalBalanceSnapshot>(snapshots.Count);
            foreach (var s in snapshots)
            {
                var key = new AccountingBalanceKey(s.AccountId, s.DimensionSetId);

                if (!snapByKey.TryAdd(key, s))
                {
                    throw new NgbInvariantViolationException(
                        message: "Operational balance reader returned duplicate keys.",
                        context: new Dictionary<string, object?>
                        {
                            ["period"] = period.ToString("yyyy-MM-dd"),
                            ["accountId"] = s.AccountId,
                            ["dimensionSetId"] = s.DimensionSetId
                        });
                }
            }

            // Sum deltas for this posting per key (using account's NormalBalance).
            var postingDelta = new Dictionary<AccountingBalanceKey, decimal>();

            foreach (var e in periodEntries)
            {
                ApplyDelta(
                    postingDelta,
                    new AccountingBalanceKey(e.Debit.Id, e.DebitDimensionSetId),
                    isNormalSide: e.Debit.NormalBalance == NormalBalance.Debit,
                    amount: e.Amount);

                ApplyDelta(
                    postingDelta,
                    new AccountingBalanceKey(e.Credit.Id, e.CreditDimensionSetId),
                    isNormalSide: e.Credit.NormalBalance == NormalBalance.Credit,
                    amount: e.Amount);
            }

            var forbids = new List<string>();

            foreach (var key in keys)
            {
                if (!accountsByKey.TryGetValue(key, out var acc))
                    continue;

                if (acc.NegativeBalancePolicy == NegativeBalancePolicy.Allow)
                    continue;

                snapByKey.TryGetValue(key, out var snap);

                // NOTE: balances table stores signed balance as (debit - credit).
                // For credit-normal accounts (including contra assets), a normal balance is NEGATIVE.
                // Here we operate in a *presented* sign convention: positive means "normal side".
                // Therefore, we must convert PreviousClosingBalance into presented sign using account.NormalBalance.
                var baseBalance = 0m;
                if (snap is not null)
                {
                    var presentedPrevClosing = acc.NormalBalance == NormalBalance.Debit
                        ? snap.PreviousClosingBalance
                        : -snap.PreviousClosingBalance;

                    baseBalance = presentedPrevClosing + TurnoverDelta(acc, snap.DebitTurnover, snap.CreditTurnover);
                }

                postingDelta.TryGetValue(key, out var delta);

                var projected = baseBalance + delta;

                if (projected >= 0)
                    continue;

                var msg = $"Negative balance projected: {acc.Code} ({acc.Name}, {acc.Type}, policy={acc.NegativeBalancePolicy}) = {projected} period={period:yyyy-MM-dd}";

                if (acc.NegativeBalancePolicy == NegativeBalancePolicy.Forbid)
                    forbids.Add(msg);
                else
                    logger.LogWarning("WARN: {Message}", msg);
            }

            if (forbids.Count > 0)
                throw new AccountingNegativeBalanceForbiddenException(string.Join(Environment.NewLine, forbids));
        }
    }

    private static void ApplyDelta(
        IDictionary<AccountingBalanceKey, decimal> deltas,
        AccountingBalanceKey key,
        bool isNormalSide,
        decimal amount)
    {
        // For a given account key, increase balance when the entry hits the account's normal side,
        // otherwise decrease. Contra is already embedded into account.NormalBalance.
        var signed = isNormalSide ? amount : -amount;

        if (deltas.TryGetValue(key, out var current))
            deltas[key] = current + signed;
        else
            deltas[key] = signed;
    }

    private static decimal TurnoverDelta(Account account, decimal debitTurnover, decimal creditTurnover)
        => account.NormalBalance == NormalBalance.Debit
            ? debitTurnover - creditTurnover
            : creditTurnover - debitTurnover;

    private async Task EnsurePeriodsNotClosedAsync(
        PostingOperation operation,
        IReadOnlyList<AccountingEntry> entries,
        CancellationToken ct)
    {
        var periods = entries
            .Select(e => AccountingPeriod.FromDateTime(e.Period))
            .Distinct()
            .OrderBy(p => p) // keep deterministic order for diagnostics/predictability
            .ToList();

        foreach (var p in periods)
        {
            if (await closedPeriodRepository.IsClosedAsync(p, ct))
                throw new PostingPeriodClosedException(operation.ToString(), p);
        }
    }
}
