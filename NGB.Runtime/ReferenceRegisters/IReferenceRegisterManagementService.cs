using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Runtime orchestration for Reference Registers (metadata + fields + key rules).
///
/// Notes:
/// - Provider-agnostic business logic; persistence is implemented in NGB.PostgreSql.
/// - Writes are strict no-op (no audit event) if they would not change any data.
/// </summary>
public interface IReferenceRegisterManagementService
{
    Task<Guid> UpsertAsync(
        string code,
        string name,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        CancellationToken ct = default);

    Task ReplaceFieldsAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterFieldDefinition> fields,
        CancellationToken ct = default);

    Task ReplaceDimensionRulesAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterDimensionRule> rules,
        CancellationToken ct = default);
}
