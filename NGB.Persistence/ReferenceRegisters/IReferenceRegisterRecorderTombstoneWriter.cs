namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Optimized append-only “storno-like” tombstone writer for reference registers.
///
/// Implementations must preserve append-only invariants by INSERTing new rows with <c>is_deleted = TRUE</c>
/// and copying field values from the last (effective) record version for each key.
///
/// This is used by document Unpost/Repost flows for <c>SubordinateToRecorder</c> reference registers.
/// </summary>
public interface IReferenceRegisterRecorderTombstoneWriter
{
    /// <summary>
    /// Appends tombstones for the recorder's currently active keys.
    ///
    /// Periodic registers nuance:
    /// - A single key (DimensionSetId + RecorderDocumentId) can have multiple effective versions across time,
    ///   distinguished by PeriodUtc/PeriodBucketUtc.
    /// - Implementations should tombstone ALL effective versions produced by the recorder (including future-effective periods),
    ///   by appending tombstone rows for each distinct (DimensionSetId, RecorderDocumentId, PeriodBucketUtc, PeriodUtc) version.
    ///
    /// If <paramref name="keepDimensionSetIds"/> is provided, only keys NOT present in that set will be tombstoned.
    /// Passing an empty set is equivalent to tombstoning all keys.
    /// </summary>
    Task AppendTombstonesForRecorderAsync(
        Guid registerId,
        Guid recorderDocumentId,
        IReadOnlyCollection<Guid>? keepDimensionSetIds,
        CancellationToken ct = default);
}
