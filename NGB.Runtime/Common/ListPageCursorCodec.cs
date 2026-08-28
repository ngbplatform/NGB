using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Common;

internal sealed record ListPageCursor(string? AfterDisplay, Guid AfterId);

internal static class ListPageCursorCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode(string? afterDisplay, Guid afterId)
    {
        if (afterId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(afterId), "Cursor ID must not be empty.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(1, afterDisplay, afterId), Json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static ListPageCursor Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentInvalidException("cursor", "Cursor must not be empty.");

        try
        {
            var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(normalized), Json);
            if (payload is not { Version: 1 } || payload.AfterId == Guid.Empty)
                throw new FormatException("Unsupported or incomplete cursor payload.");

            return new ListPageCursor(payload.AfterDisplay, payload.AfterId);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new NgbArgumentInvalidException("cursor", "Cursor is invalid.");
        }
    }

    private sealed record CursorPayload(int Version, string? AfterDisplay, Guid AfterId);
}
