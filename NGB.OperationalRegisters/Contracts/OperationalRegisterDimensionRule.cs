namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Operational register analytical dimension rule.
///
/// Table: operational_register_dimension_rules
///
/// Notes:
/// - Dimension definitions are platform-level (platform_dimensions).
/// - Ordinal is an ordering within a register, stable for UI/reporting.
/// </summary>
public sealed record OperationalRegisterDimensionRule(
    Guid DimensionId,
    string DimensionCode,
    int Ordinal,
    bool IsRequired);
