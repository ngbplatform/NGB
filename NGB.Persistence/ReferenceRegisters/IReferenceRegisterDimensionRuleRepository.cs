using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Persistence boundary for reference register dimension rules.
///
/// Table: reference_register_dimension_rules
/// </summary>
public interface IReferenceRegisterDimensionRuleRepository
{
    Task<IReadOnlyList<ReferenceRegisterDimensionRule>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces all dimension rules for the register.
    ///
    /// Notes:
    /// - Must be executed in an active transaction.
    /// - DB guards enforce append-only semantics once has_records=true.
    /// </summary>
    Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterDimensionRule> rules,
        DateTime nowUtc,
        CancellationToken ct = default);
}
