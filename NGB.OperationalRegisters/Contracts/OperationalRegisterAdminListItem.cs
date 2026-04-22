namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Admin-facing list item for operational registers.
///
/// This is a read model optimized for admin UX: it includes basic register metadata
/// plus summary counters.
/// </summary>
public sealed record OperationalRegisterAdminListItem(
    OperationalRegisterAdminItem Register,
    int ResourcesCount,
    int DimensionRulesCount);
