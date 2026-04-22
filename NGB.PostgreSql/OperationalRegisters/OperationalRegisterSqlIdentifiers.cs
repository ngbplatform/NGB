using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Backward-compatible alias for shared identifier guard.
///
/// NOTE: Keep this type to avoid churn across Operational Registers SQL code.
/// The implementation is centralized in <see cref="PostgresSqlIdentifiers"/>.
/// </summary>
internal static class OperationalRegisterSqlIdentifiers
{
    public const int MaxIdentifierLength = PostgresSqlIdentifiers.MaxIdentifierLength;

    public static void EnsureOrThrow(string value, string context)
        => PostgresSqlIdentifiers.EnsureOrThrow(value, context);
}
