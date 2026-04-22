namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// A request DTO for replacing operational register resources.
///
/// Repositories/services will normalize:
/// - CodeNorm: lower(trim(code))
/// - ColumnCode: safe SQL identifier derived from code
/// </summary>
public sealed record OperationalRegisterResourceDefinition(
    string Code,
    string Name,
    int Ordinal);
