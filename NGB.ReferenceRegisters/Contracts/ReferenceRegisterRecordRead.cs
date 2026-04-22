namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// A single reference register record version read from a per-register physical table.
/// </summary>
public sealed record ReferenceRegisterRecordRead(
    long RecordId,
    Guid DimensionSetId,
    DateTime? PeriodUtc,
    DateTime? PeriodBucketUtc,
    Guid? RecorderDocumentId,
    DateTime RecordedAtUtc,
    bool IsDeleted,
    IReadOnlyDictionary<string, object?> Values);
