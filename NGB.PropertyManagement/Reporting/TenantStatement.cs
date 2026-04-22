using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Reporting;

public sealed record TenantStatementQuery(
    Guid LeaseId,
    DateOnly? FromUtc,
    DateOnly ToUtc,
    int Offset,
    int Limit)
{
    public void EnsureInvariant()
    {
        if (LeaseId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(LeaseId), "LeaseId must not be empty.");

        if (FromUtc is not null && FromUtc.Value > ToUtc)
            throw new NgbArgumentInvalidException(nameof(FromUtc), "FromUtc must be on or before ToUtc.");

        if (Offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(Offset), Offset, "Offset must be zero or positive.");

        if (Limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(Limit), Limit, "Limit must be positive.");
    }
}

public sealed record TenantStatementRow(
    DateOnly OccurredOnUtc,
    Guid DocumentId,
    string DocumentType,
    string DocumentDisplay,
    string EntryTypeDisplay,
    string? Description,
    decimal ChargeAmount,
    decimal CreditAmount,
    decimal RunningBalance)
{
    public void EnsureInvariant()
    {
        if (DocumentId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(DocumentId), "DocumentId must not be empty.");

        if (string.IsNullOrWhiteSpace(DocumentType))
            throw new NgbArgumentInvalidException(nameof(DocumentType), "DocumentType is required.");

        if (string.IsNullOrWhiteSpace(DocumentDisplay))
            throw new NgbArgumentInvalidException(nameof(DocumentDisplay), "DocumentDisplay is required.");

        if (string.IsNullOrWhiteSpace(EntryTypeDisplay))
            throw new NgbArgumentInvalidException(nameof(EntryTypeDisplay), "EntryTypeDisplay is required.");

        if (ChargeAmount < 0m)
            throw new NgbArgumentInvalidException(nameof(ChargeAmount), "ChargeAmount must not be negative.");

        if (CreditAmount < 0m)
            throw new NgbArgumentInvalidException(nameof(CreditAmount), "CreditAmount must not be negative.");

        if (ChargeAmount > 0m && CreditAmount > 0m)
            throw new NgbArgumentInvalidException(nameof(ChargeAmount), "A statement row cannot contain both a charge and a credit amount.");
    }
}

public sealed record TenantStatementTotals(
    DateOnly? FromUtc,
    DateOnly ToUtc,
    decimal OpeningBalance,
    decimal TotalCharges,
    decimal TotalCredits,
    decimal ClosingBalance)
{
    public void EnsureInvariant()
    {
        if (TotalCharges < 0m)
            throw new NgbArgumentInvalidException(nameof(TotalCharges), "TotalCharges must not be negative.");

        if (TotalCredits < 0m)
            throw new NgbArgumentInvalidException(nameof(TotalCredits), "TotalCredits must not be negative.");
    }
}

public sealed record TenantStatementPage(
    IReadOnlyList<TenantStatementRow> Rows,
    int Total,
    TenantStatementTotals Totals)
{
    public void EnsureInvariant()
    {
        if (Rows is null)
            throw new NgbArgumentRequiredException(nameof(Rows));

        if (Total < 0)
            throw new NgbArgumentInvalidException(nameof(Total), "Total must not be negative.");

        Totals.EnsureInvariant();

        foreach (var row in Rows)
        {
            row.EnsureInvariant();
        }
    }
}
