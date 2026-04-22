using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Payables;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Payables;

public sealed class PostgresPayableApplyHeadWriter(IUnitOfWork uow) : IPayableApplyHeadWriter
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
INSERT INTO doc_pm_payable_apply
    (document_id, credit_document_id, charge_document_id, applied_on_utc, amount, memo)
VALUES
    (@DocumentId, @CreditDocumentId, @ChargeDocumentId, @AppliedOnUtc, @Amount, @Memo)
ON CONFLICT (document_id) DO UPDATE SET
    credit_document_id = EXCLUDED.credit_document_id,
    charge_document_id = EXCLUDED.charge_document_id,
    applied_on_utc = EXCLUDED.applied_on_utc,
    amount = EXCLUDED.amount,
    memo = EXCLUDED.memo;
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
}
