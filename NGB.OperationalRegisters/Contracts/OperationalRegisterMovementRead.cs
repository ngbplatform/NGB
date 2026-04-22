namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// A movement row materialized from an Operational Register's movements table.
/// </summary>
public sealed record OperationalRegisterMovementRead(
    long MovementId,
    Guid DocumentId,
    DateTime OccurredAtUtc,
    Guid DimensionSetId,
    bool IsStorno,
    IReadOnlyDictionary<string, decimal> Resources);
