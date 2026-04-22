using NGB.Tools.Normalization;

namespace NGB.OperationalRegisters;

/// <summary>
/// Operational Registers — DB naming convention.
///
/// Terms:
/// - Operational Register: a document-driven, append-only register of movements with derived turnovers and balances.
///
/// Prefixes:
/// - Common metadata tables are prefixed with 'operational_register_' (plural).
/// - Per-register fact tables are prefixed with 'opreg_'.
///
/// Code normalization:
/// - code_norm (business identifier) = lower(trim(code)).
/// - table_code (for physical table names) is a stricter, ASCII-only normalization.
/// - column_code (for physical column names) uses the same rules as table_code.
///
/// Per-register tables (code_norm = X, table_code = NormalizeTableCode(code_norm)):
/// - opreg_<X>__movements
/// - opreg_<X>__turnovers
/// - opreg_<X>__balances
///
/// IMPORTANT:
/// - Different code_norm values may normalize to the same table_code (e.g. "a-b" and "a_b" => "a_b").
/// - DB enforces uniqueness of table_code in operational_registers (fail-fast).
///
/// Constraints / Indexes / Triggers:
/// - pk_<table>
/// - ux_<table>__<col1>_<col2>...
/// - ix_<table>__<col1>_<col2>...
/// - fk_<table>__<column>__<ref_table>
/// - ck_<table>__<name>
/// - trg_<table>__<name>
/// - fn_<name>
/// </summary>
public static class OperationalRegisterNaming
{
    // SQL identifier length limit used by the current storage provider (PostgreSQL: 63 chars for ASCII).
    // Our per-register fact tables use the naming pattern:
    //   opreg_<table_code>__movements / __turnovers / __balances
    // The longest suffix is '__movements' (11 chars), and the prefix is 'opreg_' (6 chars),
    // so the maximum safe length for <table_code> is 63 - (6 + 11) = 46.
    private const int MaxSqlIdentifierLen = 63;
    private const int MaxTableCodeLen = 46;
    public static string MovementsTable(string registerCode) => $"opreg_{NormalizeTableCode(registerCode)}__movements";
    public static string TurnoversTable(string registerCode) => $"opreg_{NormalizeTableCode(registerCode)}__turnovers";
    public static string BalancesTable(string registerCode) => $"opreg_{NormalizeTableCode(registerCode)}__balances";

    /// <summary>
    /// Strict normalization for SQL identifiers.
    ///
    /// Rules:
    /// - lower
    /// - non [a-z0-9] => '_'
    /// - collapse multiple '_' into one
    /// - trim '_' from both ends
    /// </summary>
    public static string NormalizeTableCode(string code)
    {
        // Ensure per-register table names always fit into Postgres identifier limit.
        // If we need to truncate, add a deterministic hash suffix to avoid collisions.
        return IdentifierNormalization.NormalizeStrictTableCode(
            code,
            nameof(code),
            emptyResultMessage: "Code normalizes to an empty table code.",
            maxTableCodeLen: MaxTableCodeLen);
    }

    /// <summary>
    /// Normalizes a code to a safe SQL identifier for a physical column name.
    /// Uses the same normalization rules as <see cref="NormalizeTableCode"/>.
    /// </summary>
    public static string NormalizeColumnCode(string code)
    {
        // For physical columns, we can use the full Postgres identifier length.
        // However, if we truncate we must add a deterministic suffix to avoid collisions.
        return IdentifierNormalization.NormalizeStrictColumnCode(
            code,
            nameof(code),
            emptyResultMessage: "Code normalizes to an empty token.",
            maxSqlIdentifierLen: MaxSqlIdentifierLen,
            digitPrefix: "r_");
    }
}
