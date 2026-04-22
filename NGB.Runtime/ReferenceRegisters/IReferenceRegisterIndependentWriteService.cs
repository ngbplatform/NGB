using NGB.Core.Dimensions;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Independent-mode (no recorder) reference register write API.
///
/// Writes are append-only. "Update" is implemented by inserting a new version.
/// "Delete" is implemented by inserting a tombstone record (IsDeleted=true).
///
/// Both operations are idempotent via commandId.
/// </summary>
public interface IReferenceRegisterIndependentWriteService
{
    Task<ReferenceRegisterWriteResult> UpsertAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        DateTime? periodUtc,
        IReadOnlyDictionary<string, object?> values,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task<ReferenceRegisterWriteResult> UpsertByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime? periodUtc,
        IReadOnlyDictionary<string, object?> values,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task<ReferenceRegisterWriteResult> TombstoneAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        DateTime asOfUtc,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task<ReferenceRegisterWriteResult> TombstoneByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid commandId,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
