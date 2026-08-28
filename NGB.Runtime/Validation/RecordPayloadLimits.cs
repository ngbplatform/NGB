using NGB.Contracts.Common;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Validation;

internal static class RecordPayloadLimits
{
    internal const int MaxParts = 50;
    internal const int MaxRows = 5_000;
    internal const int MaxCells = 100_000;

    internal static void EnsureWithinLimits(IReadOnlyDictionary<string, RecordPartPayload> parts)
    {
        if (parts.Count > MaxParts)
        {
            throw new NgbArgumentOutOfRangeException(
                "payload.parts",
                parts.Count,
                $"At most {MaxParts} tabular parts are allowed per payload.");
        }

        long rowCount = 0;
        long cellCount = 0;

        foreach (var part in parts.Values)
        {
            var rows = part?.Rows;
            if (rows is null)
                continue;

            rowCount += rows.Count;
            if (rowCount > MaxRows)
            {
                throw new NgbArgumentOutOfRangeException(
                    "payload.partRows",
                    rowCount,
                    $"At most {MaxRows:N0} tabular rows are allowed per payload.");
            }

            foreach (var row in rows)
            {
                cellCount += row?.Count ?? 0;
                if (cellCount > MaxCells)
                {
                    throw new NgbArgumentOutOfRangeException(
                        "payload.partCells",
                        cellCount,
                        $"At most {MaxCells:N0} tabular cells are allowed per payload.");
                }
            }
        }
    }
}
