using System.Globalization;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class RenderedSheetCursorCodec
{
    private const string OffsetOnlyPrefix = "v1";
    private const string SnapshotPrefix = "v2";

    public static string EncodeOffsetOnly(int offset)
    {
        if (offset < 0)
            throw new NgbArgumentInvalidException(nameof(offset), "Cursor offset must be zero or greater.");

        return $"{OffsetOnlyPrefix}|{offset.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string EncodeSnapshot(Guid snapshotId, int offset, Guid fingerprint)
    {
        if (snapshotId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(snapshotId), "Snapshot id must be a non-empty GUID.");

        if (fingerprint == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(fingerprint), "Fingerprint must be a non-empty GUID.");

        if (offset < 0)
            throw new NgbArgumentInvalidException(nameof(offset), "Cursor offset must be zero or greater.");

        return string.Join(
            '|',
            SnapshotPrefix,
            snapshotId.ToString("D", CultureInfo.InvariantCulture),
            offset.ToString(CultureInfo.InvariantCulture),
            fingerprint.ToString("D", CultureInfo.InvariantCulture));
    }

    public static RenderedSheetCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && string.Equals(parts[0], OffsetOnlyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) || offset < 0)
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor offset.");

            return new RenderedSheetCursor(offset, SnapshotId: null, Fingerprint: null);
        }

        if (parts.Length == 4 && string.Equals(parts[0], SnapshotPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(parts[1], out var snapshotId) || snapshotId == Guid.Empty)
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor snapshot id.");

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) || offset < 0)
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor offset.");

            if (!Guid.TryParse(parts[3], out var fingerprint) || fingerprint == Guid.Empty)
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor fingerprint.");

            return new RenderedSheetCursor(offset, snapshotId, fingerprint);
        }

        throw new NgbArgumentInvalidException("cursor", "Invalid cursor format.");
    }
}

internal sealed record RenderedSheetCursor(int Offset, Guid? SnapshotId, Guid? Fingerprint);
