using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Persistence.Locks;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;

namespace NGB.Runtime.Posting;

public sealed class UnpostingService(
    PostingEngine engine,
    IAccountingEntryReader entryReader,
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks)
{
    public async Task UnpostAsync(Guid documentId, CancellationToken ct = default)
    {
        // Serialize all operations on the same document id.
        // This prevents races like Unpost vs Repost where both sides read the same old state and then write storno twice.
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await advisoryLocks.LockDocumentAsync(documentId, innerCt);

            var oldEntries = await entryReader.GetByDocumentAsync(documentId, innerCt);
            if (oldEntries.Count == 0)
                return;

            var stornoEntries = AccountingStornoFactory.Create(oldEntries);

            // Important: use ct-aware overload + manageTransaction=false to keep the outer transaction.
            await engine.PostAsync(
                PostingOperation.Unpost,
                (ctx, _) =>
                {
                    foreach (var s in stornoEntries)
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

                    return Task.CompletedTask;
                },
                manageTransaction: false,
                ct: innerCt);
        }, ct);
    }
}
