using NGB.Core.Dimensions;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

public interface IReferenceRegisterReadService
{
    Task<ReferenceRegisterRecordRead?> SliceLastAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<ReferenceRegisterRecordRead?> SliceLastByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    /// <summary>
    /// Cursor-aware variant of <see cref="SliceLastAllAsync"/>. Use this when you want stable pagination in presence
    /// of tombstones (deleted keys). The returned cursor advances by the last key examined by the service, which may
    /// be greater than the last returned visible key.
    /// </summary>
    Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    /// <summary>
    /// Cursor-aware variant of <see cref="SliceLastAllFilteredAsync"/>.
    /// </summary>
    Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllFilteredPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    /// <summary>
    /// Read-optimized helper for UI/reporting: returns SliceLastAll (optionally filtered by required dimensions),
    /// plus resolved DimensionBag and DimensionValue display strings.
    /// </summary>
    Task<IReadOnlyList<ReferenceRegisterRecordSnapshot>> SliceLastAllEnrichedAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions = null,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    /// <summary>
    /// Cursor-aware variant of <see cref="SliceLastAllEnrichedAsync"/>.
    /// </summary>
    Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordSnapshot>> SliceLastAllEnrichedPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions = null,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceRegisterRecordRead>> GetKeyHistoryAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceRegisterRecordRead>> GetKeyHistoryByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default);
}
