using NGB.Metadata.Base;

namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Read model for reference register fields.
///
/// Source: reference_register_fields
/// </summary>
public sealed record ReferenceRegisterField(
    Guid RegisterId,
    string Code,
    string CodeNorm,
    string ColumnCode,
    string Name,
    int Ordinal,
    ColumnType ColumnType,
    bool IsNullable,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
