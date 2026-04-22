namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Write model for creating/updating operational register metadata.
///
/// Table: operational_registers
/// </summary>
public sealed record OperationalRegisterUpsert(
    Guid RegisterId,
    string Code,
    string Name);
