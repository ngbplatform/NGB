namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Admin-facing operational register details.
///
/// Intended for UI screens that allow viewing and editing the register metadata.
/// </summary>
public sealed record OperationalRegisterAdminDetails(
    OperationalRegisterAdminItem Register,
    IReadOnlyList<OperationalRegisterResource> Resources,
    IReadOnlyList<OperationalRegisterDimensionRule> DimensionRules);
 