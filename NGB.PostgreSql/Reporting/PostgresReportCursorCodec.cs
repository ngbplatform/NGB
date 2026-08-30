using System.Globalization;
using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

internal static class PostgresReportCursorCodec
{
    private const int CurrentVersion = 1;

    public static string Encode(
        string datasetCode,
        IReadOnlyList<PostgresReportCursorColumn> columns,
        IReadOnlyDictionary<string, object?> row)
    {
        var values = columns
            .Select(column => row.TryGetValue(column.Alias, out var value)
                ? EncodeValue(value)
                : throw new NgbInvariantViolationException($"PostgreSQL reporting cursor column '{column.Alias}' is missing from the materialized row."))
            .ToArray();

        var payload = new CursorPayload(CurrentVersion, datasetCode, BuildSignature(columns), values);
        return Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    public static IReadOnlyList<object?> Decode(
        string cursor,
        string datasetCode,
        IReadOnlyList<PostgresReportCursorColumn> columns)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            throw InvalidCursor();

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(Base64UrlDecode(cursor));
            if (payload is null
                || payload.Version != CurrentVersion
                || string.IsNullOrWhiteSpace(payload.DatasetCode)
                || !payload.DatasetCode.Equals(datasetCode, StringComparison.OrdinalIgnoreCase)
                || payload.Signature is null
                || !payload.Signature.Equals(BuildSignature(columns), StringComparison.Ordinal)
                || payload.Values is null
                || payload.Values.Length != columns.Count)
            {
                throw InvalidCursor();
            }

            return payload.Values.Select(DecodeValue).ToArray();
        }
        catch (NgbArgumentInvalidException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or OverflowException)
        {
            throw InvalidCursor();
        }
    }

    private static EncodedValue EncodeValue(object? value)
        => value switch
        {
            null => new EncodedValue("null", null),
            string text => new EncodedValue("string", text),
            Guid guid => new EncodedValue("guid", guid.ToString("D")),
            DateTime dateTime => new EncodedValue("datetime", dateTime.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => new EncodedValue("datetimeoffset", dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
            DateOnly date => new EncodedValue("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            bool boolean => new EncodedValue("bool", boolean ? "true" : "false"),
            byte or sbyte or short or ushort or int or uint or long => new EncodedValue("int64", Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            decimal number => new EncodedValue("decimal", number.ToString(CultureInfo.InvariantCulture)),
            float or double => new EncodedValue("double", Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
            _ => throw new NgbInvariantViolationException($"PostgreSQL reporting cursor does not support value type '{value.GetType().FullName}'.")
        };

    private static object? DecodeValue(EncodedValue value)
        => value.Type switch
        {
            "null" when value.Value is null => null,
            "string" => value.Value ?? throw InvalidCursor(),
            "guid" => Guid.Parse(value.Value ?? throw InvalidCursor()),
            "datetime" => DateTime.Parse(value.Value ?? throw InvalidCursor(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "datetimeoffset" => DateTimeOffset.Parse(value.Value ?? throw InvalidCursor(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            "date" => DateOnly.ParseExact(value.Value ?? throw InvalidCursor(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            "bool" => bool.Parse(value.Value ?? throw InvalidCursor()),
            "int64" => long.Parse(value.Value ?? throw InvalidCursor(), NumberStyles.Integer, CultureInfo.InvariantCulture),
            "decimal" => decimal.Parse(value.Value ?? throw InvalidCursor(), NumberStyles.Number, CultureInfo.InvariantCulture),
            "double" => double.Parse(value.Value ?? throw InvalidCursor(), NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => throw InvalidCursor()
        };

    private static string BuildSignature(IReadOnlyList<PostgresReportCursorColumn> columns)
        => string.Join('|', columns.Select(x => $"{x.Alias}:{x.DataType}:{x.Direction}"));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException("Invalid base64url length.")
        };
        return Convert.FromBase64String(normalized);
    }

    private static NgbArgumentInvalidException InvalidCursor()
        => new("cursor", "The composable report cursor is invalid, expired, or belongs to another report layout.");

    private sealed record CursorPayload(
        int Version,
        string DatasetCode,
        string Signature,
        EncodedValue[] Values);

    private sealed record EncodedValue(string Type, string? Value);
}
