using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Persistence boundary for reference register fields.
///
/// Table: reference_register_fields
/// </summary>
public interface IReferenceRegisterFieldRepository
{
    Task<IReadOnlyList<ReferenceRegisterField>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces all fields for the register.
    ///
    /// Notes:
    /// - Must be executed in an active transaction.
    /// - DB guards enforce immutability of identifiers once has_records=true.
    /// </summary>
    Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterFieldDefinition> fields,
        DateTime nowUtc,
        CancellationToken ct = default);
}
