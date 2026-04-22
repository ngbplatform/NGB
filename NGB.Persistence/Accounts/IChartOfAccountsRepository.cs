using NGB.Accounting.Accounts;

namespace NGB.Persistence.Accounts;

/// <summary>
/// Persistence boundary for Chart of Accounts.
///
/// IMPORTANT:
/// - This repository is intended for a single-company (single-DB) tenant model.
/// - Consumers should treat Accounts as immutable once referenced by movements.
///   (Enforced via service-level rules, not by this interface.)
/// </summary>
public interface IChartOfAccountsRepository
{
    /// <summary>
    /// Returns accounts to be used by Runtime (posting/closing/validation/reporting snapshots).
    ///
    /// By convention this method returns ONLY active accounts and excludes soft-deleted ones.
    /// </summary>
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns an admin-facing projection of the chart of accounts.
    ///
    /// This method is intended for UI/administration screens and MAY include inactive and/or
    /// soft-deleted accounts depending on parameters.
    /// </summary>
    Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(
        bool includeDeleted = false,
        CancellationToken ct = default);

    /// <summary>
    /// Returns an admin-facing projection by account id, or null if not found.
    /// </summary>
    Task<ChartOfAccountsAdminItem?> GetAdminByIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Returns admin projections for the requested account ids.
    ///
    /// Notes:
    /// - This method may return inactive and/or deleted accounts (the caller decides how to interpret).
    /// - If <paramref name="accountIds"/> is empty, returns an empty list.
    /// </summary>
    Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAdminByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the account is referenced by any movements (register entries).
    /// </summary>
    Task<bool> HasMovementsAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new account row. Requires an active transaction.
    /// </summary>
    Task CreateAsync(Account account, bool isActive = true, CancellationToken ct = default);

    /// <summary>
    /// Returns account code by account id (for reporting/display).
    /// Returns null if the account does not exist.
    /// </summary>
    Task<string?> GetCodeByIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing account row (excluding delete flag). Requires an active transaction.
    /// </summary>
    Task UpdateAsync(Account account, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Sets account active flag. Requires an active transaction.
    /// </summary>
    Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Marks an account for deletion (is_deleted = true). Requires an active transaction.
    /// </summary>
    Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Unmarks an account for deletion (is_deleted = false).
    /// </summary>
    Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default);
}
