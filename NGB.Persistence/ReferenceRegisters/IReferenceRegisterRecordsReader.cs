using NGB.Core.Dimensions;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Read boundary for per-reference-register physical records tables.
///
/// Primary UI/report use-case is SliceLast ("latest version as-of moment").
/// </summary>
public interface IReferenceRegisterRecordsReader
{
    /// <summary>
    /// Returns the last record version for the specified key as of the given moment.
    ///
    /// Notes:
    /// - Tables are append-only; deletions are represented as a new version with <see cref="ReferenceRegisterRecordWrite.IsDeleted"/> = true.
    /// - Therefore, the returned record can be marked as deleted.
    /// - If the physical records table does not exist yet, returns <c>null</c>.
    /// </summary>
    Task<ReferenceRegisterRecordRead?> SliceLastAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the last record version for the specified key that is EFFECTIVE as of <paramref name="effectiveAsOfUtc"/>,
    /// while considering only versions RECORDED not after <paramref name="recordedAsOfUtc"/>.
    ///
    /// This is used by independent-mode writes to compute the previous value at an effective moment even when a write is backdated
    /// (i.e. <c>PeriodUtc</c> is in the past, but the row is recorded now).
    ///
    /// For non-periodic registers, <paramref name="effectiveAsOfUtc"/> is ignored and the method behaves like
    /// <see cref="SliceLastAsync"/> with <paramref name="recordedAsOfUtc"/>.
    /// </summary>
    Task<ReferenceRegisterRecordRead?> SliceLastForEffectiveMomentAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime effectiveAsOfUtc,
        DateTime recordedAsOfUtc,
        Guid? recorderDocumentId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the latest record version for each key (dimension_set_id + recorder_document_id) as of the given moment.
    ///
    /// This is a building block for showing the "current state" of a reference register.
    ///
    /// Notes:
    /// - For <see cref="ReferenceRegisterRecordMode.SubordinateToRecorder"/>, <paramref name="recorderDocumentId"/> is required.
    /// - The returned records can be tombstones (<c>IsDeleted=true</c>).
    /// - Pagination is key-based (by DimensionSetId) for deterministic traversal.
    /// </summary>
    Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="SliceLastAllAsync"/>, but filters the keys by requiring that DimensionSet contains ALL provided pairs
    /// (DimensionId -&gt; ValueId). This enables selections by dimensions without knowing DimensionSetId upfront.
    ///
    /// Notes:
    /// - <paramref name="requiredDimensions"/> must be non-empty and contain unique DimensionId entries.
    /// - For <see cref="ReferenceRegisterRecordMode.SubordinateToRecorder"/>, <paramref name="recorderDocumentId"/> is required.
    /// </summary>
    Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredByDimensionsAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>
    /// Returns physical reference register rows written by the specified recorder document,
    /// ordered by RecordedAtUtc DESC, RecordId DESC.
    ///
    /// Notes:
    /// - Intended for document-centric effects viewers.
    /// - For registers with <see cref="ReferenceRegisterRecordMode.Independent"/>, returns an empty list because
    ///   rows do not carry recorder_document_id and therefore cannot be attributed exactly to a document.
    /// - Pagination is version-based using the tuple (RecordedAtUtc, RecordId).
    /// </summary>
    Task<IReadOnlyList<ReferenceRegisterRecordRead>> ListByRecorderDocumentAsync(
        Guid registerId,
        Guid recorderDocumentId,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full version history for a single key (dimension_set_id + recorder_document_id) as of the given moment,
    /// ordered by RecordedAtUtc DESC, RecordId DESC.
    ///
    /// For periodic registers, <paramref name="periodUtc"/> must be provided; history is returned only for the corresponding
    /// period bucket.
    ///
    /// Pagination is version-based using the tuple (RecordedAtUtc, RecordId).
    /// To request the next page of older versions, pass both <paramref name="beforeRecordedAtUtc"/> and <paramref name="beforeRecordId"/>
    /// from the last row of the previous page.
    ///
    /// Notes:
    /// - Tables are append-only; deletions are represented as a new version with IsDeleted=true.
    /// - If the physical records table does not exist yet, returns an empty list.
    /// </summary>
    Task<IReadOnlyList<ReferenceRegisterRecordRead>> ListKeyHistoryAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        CancellationToken ct = default);
}
