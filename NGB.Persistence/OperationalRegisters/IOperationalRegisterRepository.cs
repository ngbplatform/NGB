using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Persistence boundary for operational registers registry (metadata).
///
/// Tables:
/// - operational_registers
///
/// Notes:
/// - Register id is deterministic and derived from code_norm (see NGB.OperationalRegisters.OperationalRegisterId).
/// - The repository is single-tenant (one DB = one company).
/// </summary>
public interface IOperationalRegisterRepository
{
    Task<IReadOnlyList<OperationalRegisterAdminItem>> GetAllAsync(CancellationToken ct = default);

    Task<OperationalRegisterAdminItem?> GetByIdAsync(Guid registerId, CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> registerIds,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a register by code (case-insensitive).
    /// Implementations should normalize code the same way as DB generated column <c>code_norm</c>.
    /// </summary>
    Task<OperationalRegisterAdminItem?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Loads a register by its physical table name token (<c>table_code</c>).
    ///
    /// Notes:
    /// - <c>table_code</c> is a generated column in DB derived from normalized code.
    /// - This method is primarily used to fail-fast on physical table name collisions
    ///   (e.g. "a-b" and "a_b" => both normalize to the same <c>table_code</c>).
    /// </summary>
    Task<OperationalRegisterAdminItem?> GetByTableCodeAsync(string tableCode, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a register row.
    /// Requires an active transaction.
    ///
    /// Semantics:
    /// - if row does not exist: INSERT
    /// - if row exists: update code/name (code change allowed, but must satisfy unique code_norm constraint)
    /// </summary>
    Task UpsertAsync(OperationalRegisterUpsert register, DateTime nowUtc, CancellationToken ct = default);
}
