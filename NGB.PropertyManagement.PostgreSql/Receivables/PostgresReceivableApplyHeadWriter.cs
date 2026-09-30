using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Receivables;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Receivables;

public sealed class PostgresReceivableApplyHeadWriter(IUnitOfWork uow) : IReceivableApplyHeadBatchWriter
{
    public async Task UpsertAsync(
        Guid documentId,
        Guid creditDocumentId,
        Guid chargeDocumentId,
        DateOnly appliedOnUtc,
        decimal amount,
        string? memo,
        CancellationToken ct = default)
    {
        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

        if (creditDocumentId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(creditDocumentId), "Credit Source is required.");

        if (chargeDocumentId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(chargeDocumentId), "Charge is required.");

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
INSERT INTO doc_pm_receivable_apply
    (document_id, credit_document_id, charge_document_id, applied_on_utc, amount, memo)
VALUES
    (@DocumentId, @CreditDocumentId, @ChargeDocumentId, @AppliedOnUtc, @Amount, @Memo)
ON CONFLICT (document_id) DO UPDATE SET
    credit_document_id     = EXCLUDED.credit_document_id,
    charge_document_id      = EXCLUDED.charge_document_id,
    applied_on_utc = EXCLUDED.applied_on_utc,
    amount         = EXCLUDED.amount,
    memo           = EXCLUDED.memo;
""";

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DocumentId = documentId,
                CreditDocumentId = creditDocumentId,
                ChargeDocumentId = chargeDocumentId,
                AppliedOnUtc = appliedOnUtc,
                Amount = amount,
                Memo = memo
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task UpsertManyAsync(IReadOnlyList<ReceivableApplyHeadWrite> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return;

        foreach (var item in items)
        {
            Validate(item.DocumentId, item.CreditDocumentId, item.ChargeDocumentId);
        }

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
INSERT INTO doc_pm_receivable_apply
    (document_id, credit_document_id, charge_document_id, applied_on_utc, amount, memo)
SELECT *
FROM UNNEST(
    @DocumentIds::uuid[],
    @CreditDocumentIds::uuid[],
    @ChargeDocumentIds::uuid[],
    @AppliedOnUtc::date[],
    @Amounts::numeric[],
    @Memos::text[])
ON CONFLICT (document_id) DO UPDATE SET
    credit_document_id = EXCLUDED.credit_document_id,
    charge_document_id = EXCLUDED.charge_document_id,
    applied_on_utc = EXCLUDED.applied_on_utc,
    amount = EXCLUDED.amount,
    memo = EXCLUDED.memo;
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                DocumentIds = items.Select(static item => item.DocumentId).ToArray(),
                CreditDocumentIds = items.Select(static item => item.CreditDocumentId).ToArray(),
                ChargeDocumentIds = items.Select(static item => item.ChargeDocumentId).ToArray(),
                AppliedOnUtc = items.Select(static item => item.AppliedOnUtc).ToArray(),
                Amounts = items.Select(static item => item.Amount).ToArray(),
                Memos = items.Select(static item => item.Memo).ToArray()
            },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private static void Validate(Guid documentId, Guid creditDocumentId, Guid chargeDocumentId)
    {
        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

        if (creditDocumentId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(creditDocumentId), "Credit Source is required.");

        if (chargeDocumentId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(chargeDocumentId), "Charge is required.");
    }
}
