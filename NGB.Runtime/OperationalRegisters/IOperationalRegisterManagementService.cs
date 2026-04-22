using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Runtime orchestration for Operational Registers (metadata + dimension rules).
///
/// Notes:
/// - This is provider-agnostic business logic; persistence is implemented in NGB.PostgreSql.
/// - Writes are strictly no-op (no audit event) if they would not change any data.
/// </summary>
public interface IOperationalRegisterManagementService
{
    /// <summary>
    /// Creates a new register or updates its metadata if it already exists.
    ///
    /// Deterministic id is derived from code_norm (see OperationalRegisterId).
    /// </summary>
    Task<Guid> UpsertAsync(string code, string name, CancellationToken ct = default);

    /// <summary>
    /// Replaces analytical dimension rules for a register.
    ///
    /// Semantics (append-only movements + storno):
    /// - if the register has no movements yet: full replace is allowed (DELETE + INSERT).
    /// - once ANY movements exist: rules become append-only; this method only allows adding new rules
    ///   with <c>IsRequired=false</c>. Existing rules must stay identical (same DimensionId, Ordinal, IsRequired).
    ///
    /// Strict no-op: if the rules are already equivalent, no write and no audit event occurs.
    /// </summary>
    Task ReplaceDimensionRulesAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces resource metadata (aka "measures") for a register.
    ///
    /// Notes:
    /// - resources define physical numeric columns in per-register fact tables.
    /// - Storno semantics require resource physical columns (column_code) to be stable once movements exist.
    ///
    /// Strict no-op: if the resources are already equivalent, no write and no audit event occurs.
    /// </summary>
    Task ReplaceResourcesAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources,
        CancellationToken ct = default);
}
