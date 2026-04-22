using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Locks;

/// <summary>
/// Namespaces for platform advisory locks.
///
/// We use the two-int form of PostgreSQL advisory locks:
/// <c>pg_advisory_xact_lock(int key1, int key2)</c>.
///
/// <para>
/// <c>key1</c> is a fixed "namespace" to prevent collisions across lock purposes.
/// Values are readable ASCII tags with a version byte: <c>"DOC\x01"</c>, <c>"CAT\x01"</c>, <c>"PER\x01"</c>.
/// </para>
///
/// <para>
/// Namespaces are computed via <see cref="Pack"/> to avoid "magic hex" values and keep the mapping obvious.
/// </para>
/// </summary>
public static class AdvisoryLockNamespaces
{
    public static readonly int Document = Pack("DOC", 1);
    public static readonly int Catalog  = Pack("CAT", 1);
    public static readonly int Period   = Pack("PER", 1);

    // Operational Registers: month-level period lock for movement writes + finalization.
    // Separate from accounting Period to avoid unnecessary cross-subsystem serialization.
    public static readonly int OperationalRegisterPeriod = Pack("ORP", 1);

    // Operational Registers: register-level serialization for projection-chain-sensitive operations.
    public static readonly int OperationalRegister = Pack("ORR", 1);

    // Operational Registers: protect dynamic DDL (CREATE TABLE / ALTER TABLE / CREATE TRIGGER)
    // executed by EnsureSchemaAsync() for per-register tables.
    public static readonly int OperationalRegisterSchema = Pack("ORS", 1);

    // Reference Registers: protect dynamic DDL (CREATE TABLE / ALTER TABLE / CREATE TRIGGER)
    // executed by EnsureSchemaAsync() for per-register records tables.
    public static readonly int ReferenceRegisterSchema = Pack("RRS", 1);

    // Reference Registers: serialize independent writes for a given key (dimension set).
    public static readonly int ReferenceRegisterKey = Pack("RRK", 1);

    /// <summary>
    /// Formats a packed namespace value as a human-readable tag, e.g. <c>"PER\x01"</c>.
    /// Useful in logs and diagnostics.
    /// </summary>
    public static string Format(int ns)
    {
        var u = unchecked((uint)ns);

        var b0 = (byte)(u >> 24);
        var b1 = (byte)(u >> 16);
        var b2 = (byte)(u >> 8);
        var ver = (byte)u;

        // We expect 7-bit ASCII, but keep it safe for diagnostics.
        static char SafeAscii(byte b) => b <= 0x7F ? (char)b : '?';

        var tag3 = string.Concat(SafeAscii(b0), SafeAscii(b1), SafeAscii(b2));
        return $"{tag3}\\x{ver:X2}";
    }

    /// <summary>
    /// Packs a 3-character ASCII tag and a version byte into a stable 32-bit namespace value.
    /// Format (big-endian bytes): [tag3[0], tag3[1], tag3[2], version].
    ///
    /// Examples:
    /// <list type="bullet">
    /// <item><description><c>Pack("DOC", 1) == 0x444F4301</c></description></item>
    /// <item><description><c>Pack("PER", 1) == 0x50455201</c></description></item>
    /// </list>
    /// </summary>
    public static int Pack(string tag3, byte version)
    {
        if (tag3 is null)
            throw new NgbArgumentRequiredException(nameof(tag3));

        if (tag3.Length != 3)
            throw new NgbArgumentInvalidException(nameof(tag3), "tag3 must be exactly 3 ASCII characters.");

        // We require 7-bit ASCII so the mapping is unambiguous.
        for (var i = 0; i < 3; i++)
        {
            if (tag3[i] > 0x7F)
                throw new NgbArgumentInvalidException(nameof(tag3), "tag3 must contain 7-bit ASCII characters only.");
        }

        return (tag3[0] << 24) | (tag3[1] << 16) | (tag3[2] << 8) | version;
    }
}
