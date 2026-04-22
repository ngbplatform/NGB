using System.Globalization;
using System.Text.Json;

namespace NGB.AgencyBilling.Runtime.Validation;

internal static class AgencyBillingValidationValueReaders
{
    public static string? ReadString(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawString(raw) : null;

    public static string? ReadString(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawString(raw) : null;

    public static Guid? ReadGuid(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawGuid(raw) : null;

    public static Guid? ReadGuid(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawGuid(raw) : null;

    public static decimal? ReadDecimal(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawDecimal(raw) : null;

    public static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawDecimal(raw) : null;

    public static DateOnly? ReadDate(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawDate(raw) : null;

    public static DateOnly? ReadDate(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawDate(raw) : null;

    public static int? ReadInt32(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawInt32(raw) : null;

    public static int? ReadInt32(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawInt32(raw) : null;

    public static bool? ReadBoolean(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var raw) ? ReadRawBoolean(raw) : null;

    public static bool? ReadBoolean(IReadOnlyDictionary<string, JsonElement>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var raw) ? ReadRawBoolean(raw) : null;

    private static string? ReadRawString(object? raw)
        => raw switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
            JsonElement el => el.ToString(),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture)
        };

    private static Guid? ReadRawGuid(object? raw)
        => raw switch
        {
            null => null,
            Guid g => g,
            string s when Guid.TryParse(s, out var g) => g,
            JsonElement { ValueKind: JsonValueKind.String } el when Guid.TryParse(el.GetString(), out var g) => g,
            _ => null
        };

    private static decimal? ReadRawDecimal(object? raw)
        => raw switch
        {
            null => null,
            decimal d => d,
            int i => i,
            long l => l,
            float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
            double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            JsonElement { ValueKind: JsonValueKind.Number } el when el.TryGetDecimal(out var d) => d,
            JsonElement { ValueKind: JsonValueKind.String } el when decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };

    private static DateOnly? ReadRawDate(object? raw)
        => raw switch
        {
            null => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
            JsonElement { ValueKind: JsonValueKind.String } el when DateOnly.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
            _ => null
        };

    private static int? ReadRawInt32(object? raw)
        => raw switch
        {
            null => null,
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            decimal d => decimal.ToInt32(d),
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            JsonElement { ValueKind: JsonValueKind.Number } el when el.TryGetInt32(out var i) => i,
            JsonElement { ValueKind: JsonValueKind.String } el when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };

    private static bool? ReadRawBoolean(object? raw)
        => raw switch
        {
            null => null,
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } el when bool.TryParse(el.GetString(), out var b) => b,
            JsonElement { ValueKind: JsonValueKind.Number } el when el.TryGetInt32(out var i) => i != 0,
            _ => null
        };
}
