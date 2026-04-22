namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Admin-facing reference register details.
///
/// Intended for UI screens that allow viewing and editing the register metadata.
/// </summary>
public sealed record ReferenceRegisterAdminDetails(
    ReferenceRegisterAdminItem Register,
    IReadOnlyList<ReferenceRegisterField> Fields,
    IReadOnlyList<ReferenceRegisterDimensionRule> DimensionRules);
