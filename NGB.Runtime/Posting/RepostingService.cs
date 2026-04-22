using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Persistence.Locks;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

public sealed class RepostingService(
    PostingEngine engine,
    IAccountingEntryReader reader,
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks)
{
    public async Task RepostAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> postNew,
        CancellationToken ct = default)
    {
        if (postNew is null)
            throw new NgbArgumentRequiredException(nameof(postNew));

        if (documentId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(documentId), documentId, "DocumentId must be non-empty.");

        // Serialize all operations on the same document id.
        // This prevents races like Unpost vs Repost where both sides read the same old state and then write storno twice.
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await advisoryLocks.LockDocumentAsync(documentId, innerCt);

            var oldEntries = await reader.GetByDocumentAsync(documentId, innerCt);
            if (oldEntries.Count == 0)
                throw new NgbArgumentInvalidException(nameof(documentId), "No entries to repost");

            var storno = AccountingStornoFactory.Create(oldEntries);

            // Important: use ct-aware overload + manageTransaction=false to keep the outer transaction.
            await engine.PostAsync(
                PostingOperation.Repost,
                async (ctx, postingCt) =>
                {
                    foreach (var s in storno)
                    {
                        ctx.Post(
                            documentId: s.DocumentId,
                            period: s.Period,
                            debit: s.Debit,
                            credit: s.Credit,
                            amount: s.Amount,
                            debitDimensions: s.DebitDimensions,
                            creditDimensions: s.CreditDimensions,
                            isStorno: true);
                    }

                    await postNew(ctx, postingCt);
                },
                manageTransaction: false,
                ct: innerCt);
        }, ct);
    }
}
