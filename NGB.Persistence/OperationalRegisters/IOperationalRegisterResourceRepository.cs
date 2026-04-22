using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Persistence boundary for operational register resources (aka "measures").
///
/// Table:
/// - operational_register_resources
///
/// Notes:
/// - Resources are used to define the physical columns of per-register movement/turnover/balance tables.
/// - Replace semantics are used for admin UX: the current set is the source of truth.
/// </summary>
public interface IOperationalRegisterResourceRepository
{
    Task<IReadOnlyList<OperationalRegisterResource>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces all resources for a register.
    /// Requires an active transaction.
    /// </summary>
    Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources,
        DateTime nowUtc,
        CancellationToken ct = default);
}
