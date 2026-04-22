using NGB.ReferenceRegisters;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Persistence boundary for per-reference-register physical records tables.
///
/// Per-register tables are created dynamically:
///   refreg_&lt;table_code&gt;__records
///
/// Tables are append-only. Updates/deletes must be represented as new versions.
/// </summary>
public interface IReferenceRegisterRecordsStore
{
    /// <summary>
    /// Ensures that the physical records table exists and matches the current registry definitions.
    /// Safe under concurrency.
    /// </summary>
    Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default);

    /// <summary>
    /// Appends new record versions.
    ///
    /// IMPORTANT: implementation may mark <c>reference_registers.has_records=true</c> when the first record is appended.
    /// </summary>
    Task AppendAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterRecordWrite> records,
        CancellationToken ct = default);
}
