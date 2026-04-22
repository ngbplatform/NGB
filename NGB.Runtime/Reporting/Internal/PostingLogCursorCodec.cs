using System.Globalization;
using NGB.Accounting.PostingState.Readers;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class PostingLogCursorCodec
{
    // Format: {startedAtUtc:O}|{documentId:D}|{operation:int16}
    public static string Encode(PostingStateCursor cursor)
        => $"{cursor.AfterStartedAtUtc:O}|{cursor.AfterDocumentId:D}|{cursor.AfterOperation}";

    public static PostingStateCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor format.");

        if (!DateTime.TryParse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedAtUtc))
        {
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor timestamp.");
        }
        startedAtUtc = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);

        if (!Guid.TryParse(parts[1], out var docId))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor document id.");

        if (!short.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var op))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor operation.");

        return new PostingStateCursor(startedAtUtc, docId, op);
    }
}
