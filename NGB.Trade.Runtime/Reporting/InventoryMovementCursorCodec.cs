using System.Globalization;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Reporting;

internal static class InventoryMovementCursorCodec
{
    public static string Encode(OperationalRegisterOccurredAtCursor cursor)
        => $"{cursor.AfterOccurredAtUtc:O}|{cursor.AfterMovementId}";

    public static OperationalRegisterOccurredAtCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new NgbArgumentInvalidException("cursor", "Invalid inventory movement cursor format.");

        if (!DateTime.TryParse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var occurredAtUtc))
        {
            throw new NgbArgumentInvalidException("cursor", "Invalid inventory movement cursor timestamp.");
        }

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var movementId)
            || movementId <= 0)
        {
            throw new NgbArgumentInvalidException("cursor", "Invalid inventory movement cursor id.");
        }

        return new OperationalRegisterOccurredAtCursor(
            DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            movementId);
    }
}
