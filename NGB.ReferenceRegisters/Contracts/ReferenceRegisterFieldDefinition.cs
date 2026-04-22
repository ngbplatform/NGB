using NGB.Metadata.Base;

namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Write model for defining a reference register field (aka "resource").
///
/// Table: reference_register_fields
/// </summary>
public sealed record ReferenceRegisterFieldDefinition(
    string Code,
    string Name,
    int Ordinal,
    ColumnType ColumnType,
    bool IsNullable);
