namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Operational register finalization state for a month.
///
/// Table: operational_register_finalizations
/// Primary key: (register_id, period)
/// where period is a month start.
/// </summary>
public sealed record OperationalRegisterFinalization(
    Guid RegisterId,
    DateOnly Period,
    OperationalRegisterFinalizationStatus Status,
    DateTime? FinalizedAtUtc,
    DateTime? DirtySinceUtc,
    DateTime? BlockedSinceUtc,
    string? BlockedReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
