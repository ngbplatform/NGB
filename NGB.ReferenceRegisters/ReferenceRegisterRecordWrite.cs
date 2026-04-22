namespace NGB.ReferenceRegisters;

/// <summary>
/// A single reference register record write.
///
/// Semantics:
/// - Reference register tables are append-only.
/// - Updates/deletes are represented as new versions (optionally <see cref="IsDeleted"/> = true).
///
/// Values are keyed by field <c>code_norm</c> (lower(trim(code))).
/// The concrete SQL column names are derived from the registry definitions.
/// </summary>
public sealed record ReferenceRegisterRecordWrite(
    Guid DimensionSetId,
    DateTime? PeriodUtc,
    Guid? RecorderDocumentId,
    IReadOnlyDictionary<string, object?> Values,
    bool IsDeleted = false);
