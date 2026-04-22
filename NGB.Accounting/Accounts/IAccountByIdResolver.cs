namespace NGB.Accounting.Accounts;

/// <summary>
/// Optional account lookup used by Runtime services that must handle accounts which are not present
/// in the active Chart of Accounts snapshot (e.g. inactive accounts with historic movements).
/// </summary>
public interface IAccountByIdResolver
{
    /// <summary>
    /// Returns an account by id, or null if it does not exist (or is soft-deleted).
    /// </summary>
    Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Returns a map of accountId -&gt; Account for the requested ids.
    ///
    /// Contract:
    /// - Inactive accounts MUST be returned (they are commonly needed for historic reporting/closing).
    /// - Soft-deleted accounts MUST be excluded.
    /// - If an id does not exist, it is simply absent from the returned map.
    /// - Passing an empty collection returns an empty map.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default);
}
