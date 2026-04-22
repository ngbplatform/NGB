namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Key dimension rule for a reference register.
///
/// Table: reference_register_dimension_rules
/// </summary>
public sealed record ReferenceRegisterDimensionRule(
    Guid DimensionId,
    string DimensionCode,
    int Ordinal,
    bool IsRequired);
