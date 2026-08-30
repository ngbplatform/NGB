using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Tools.Paging;

/// <summary>
/// Encodes versioned, query-bound paging state for seek-based read models.
/// The encoded value is opaque transport state, not an authorization boundary.
/// </summary>
public static class OpaqueCursorCodec
{
    private const int CurrentVersion = 1;

    public static string BuildKind(string cursorKind, params string?[] components)
    {
        if (string.IsNullOrWhiteSpace(cursorKind))
            throw new NgbArgumentRequiredException(nameof(cursorKind));

        var canonical = JsonSerializer.Serialize(components ?? []);
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return $"{cursorKind}:{fingerprint}";
    }

    public static string Encode<T>(string cursorKind, T payload)
    {
        if (string.IsNullOrWhiteSpace(cursorKind))
            throw new NgbArgumentRequiredException(nameof(cursorKind));

        var json = JsonSerializer.Serialize(new CursorEnvelope<T>(CurrentVersion, cursorKind, payload));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static T Decode<T>(string cursorKind, string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursorKind))
            throw new NgbArgumentRequiredException(nameof(cursorKind));

        if (string.IsNullOrWhiteSpace(cursor))
            throw new NgbArgumentRequiredException(nameof(cursor));

        try
        {
            var normalized = cursor.Trim().Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var envelope = JsonSerializer.Deserialize<CursorEnvelope<T>>(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));

            if (envelope is null
                || envelope.Version != CurrentVersion
                || !string.Equals(envelope.CursorKind, cursorKind, StringComparison.Ordinal))
            {
                throw InvalidCursor();
            }

            return envelope.Payload ?? throw InvalidCursor();
        }
        catch (NgbArgumentInvalidException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            throw InvalidCursor();
        }
    }

    private static NgbArgumentInvalidException InvalidCursor()
        => new("cursor", "Cursor is invalid or does not match this query.");

    private sealed record CursorEnvelope<T>(int Version, string CursorKind, T Payload);
}
