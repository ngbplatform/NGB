namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Movement to be inserted into an Operational Register's per-register movements table (<c>opreg_*__movements</c>).
///
/// Notes:
/// - Storage is append-only; storno movements are created by the store (copy with <c>is_storno = true</c>).
/// - Resource values are provided as a dictionary keyed by resource <c>column_code</c>.
/// </summary>
public sealed record OperationalRegisterMovement(
    Guid DocumentId,
    DateTime OccurredAtUtc,
    Guid DimensionSetId,
    IReadOnlyDictionary<string, decimal> Resources);
