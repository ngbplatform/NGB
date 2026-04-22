using NGB.Tools.Normalization;

namespace NGB.ReferenceRegisters;

/// <summary>
/// Reference Registers — DB naming convention.
///
/// Prefixes:
/// - Common metadata tables: reference_register_*
/// - Per-register fact tables: refreg_*
///
/// Per-register table:
/// - refreg_<table_code>__records
///
/// See PostgreSQL migrations for generated column definitions:
/// reference_registers.table_code and reference_register_fields.column_code.
/// </summary>
public static class ReferenceRegisterNaming
{
    // SQL identifier length limit used by the current storage provider (PostgreSQL: 63 chars for ASCII).
    // Our per-register fact tables use:
    //   refreg_<table_code>__records
    // prefix 'refreg_' = 7 chars
    // suffix '__records' = 9 chars
    // => max <table_code> = 63 - 16 = 47
    private const int MaxSqlIdentifierLen = 63;
    private const int MaxTableCodeLen = 47;

    public static string RecordsTable(string registerCode) => $"refreg_{NormalizeTableCode(registerCode)}__records";

    /// <summary>
    /// Strict normalization for SQL identifiers.
    ///
    /// Rules:
    /// - lower
    /// - non [a-z0-9] => '_'
    /// - collapse multiple '_' into one
    /// - trim '_' from both ends
    /// - if > max length: truncate and append deterministic md5 suffix
    /// </summary>
    public static string NormalizeTableCode(string code)
    {
        return IdentifierNormalization.NormalizeStrictTableCode(
            code,
            nameof(code),
            emptyResultMessage: "Code normalizes to an empty token.",
            maxTableCodeLen: MaxTableCodeLen);
    }

    /// <summary>
    /// Normalizes a code to a safe SQL identifier for a physical column name.
    /// Uses the same normalization rules as <see cref="NormalizeTableCode"/>.
    /// </summary>
    public static string NormalizeColumnCode(string code)
    {
        return IdentifierNormalization.NormalizeStrictColumnCode(
            code,
            nameof(code),
            emptyResultMessage: "Code normalizes to an empty token.",
            maxSqlIdentifierLen: MaxSqlIdentifierLen,
            digitPrefix: "f_");
    }
}
