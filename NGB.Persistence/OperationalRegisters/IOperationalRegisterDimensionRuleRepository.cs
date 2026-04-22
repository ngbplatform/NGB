using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Persistence boundary for operational register analytical dimension rules.
///
/// Table:
/// - operational_register_dimension_rules
///
/// Notes:
/// - Dimension values are stored via DimensionSetId (platform_dimension_sets/items).
/// - These rules are used by writers/validators and by readers for filtering/enrichment.
/// </summary>
public interface IOperationalRegisterDimensionRuleRepository
{
    Task<IReadOnlyList<OperationalRegisterDimensionRule>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Applies dimension rules for a register.
    /// Requires an active transaction.
    ///
    /// Semantics (append-only movements + storno):
    /// - if the register has no movements yet: full replace is allowed (DELETE + INSERT)
    /// - once ANY movements exist: rules become append-only; only new optional rules may be inserted.
    /// </summary>
    Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        DateTime nowUtc,
        CancellationToken ct = default);
}
