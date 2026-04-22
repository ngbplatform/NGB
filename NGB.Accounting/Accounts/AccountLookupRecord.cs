namespace NGB.Accounting.Accounts;

public sealed class AccountLookupRecord
{
    public Guid AccountId { get; init; }
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
}
