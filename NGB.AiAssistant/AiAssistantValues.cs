using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NGB.AiAssistant;

internal sealed class AiAssistantValues(TimeZoneInfo? timeZone = null)
{
    private const int MaximumValues = 512;

    private static readonly Regex EmbeddedDate = new(@"\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly string _prefix = Guid.NewGuid().ToString("N")[..12];
    private readonly Dictionary<(string Kind, string Value), AiAssistantValue> _values = new();

    internal IReadOnlyCollection<AiAssistantValue> Items => _values.Values;

    internal bool TryPrepare(object? data, int maximumBytes, out JsonNode? prepared)
    {
        var existing = _values.Keys.ToHashSet();
        prepared = Visit(JsonSerializer.SerializeToNode(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)), null);

        if (JsonSerializer.SerializeToUtf8Bytes(prepared).Length <= maximumBytes)
            return true;

        // An omitted result must not consume the display budget or publish values
        // that were never supplied to the model as evidence.
        foreach (var key in _values.Keys.Where(key => !existing.Contains(key)).ToArray())
        {
            _values.Remove(key);
        }

        prepared = null;

        return false;
    }

    internal string Bind(string answer)
    {
        // Some models copy raw values instead of references. Bind exact copies before
        // Markdown parsing, longest first, so dates inside literal content stay literal.
        var literals = _values.Values
            .Where(item => item.Value.Length > 0)
            .SelectMany(item => LiteralValues(item).Select(literal => new { literal, item }))
            .GroupBy(value => value.literal, StringComparer.Ordinal)
            .Where(group => group.Select(value => (value.item.Kind, value.item.Value)).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().item, StringComparer.Ordinal);

        if (literals.Count == 0)
            return answer;

        var alternatives = string.Join('|', literals.Keys.OrderByDescending(value => value.Length).Select(Regex.Escape));
        var pattern = @"\{\{ngb-value:[^{}]+\}\}|(?<![\w.-])(?:" + alternatives + @")(?![\w-]|\.\w)";

        return Regex.Replace(
            answer,
            pattern,
            match => literals.TryGetValue(match.Value, out var value)
                ? value.Reference
                : match.Value,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private IEnumerable<string> LiteralValues(AiAssistantValue value)
    {
        if (value.Kind == "bytes")
        {
            yield return value.Value + " bytes";
            yield return FormatBytes(value.Value);
        }
        else
        {
            yield return value.Value;
            if (value.Kind == "text" && EmbeddedDate.IsMatch(value.Value))
            {
                // Recover a copied literal whose embedded dates were formatted by the
                // model. Bind only a complete, unambiguous match to a supplied value.
                foreach (var format in new[] { "MM/dd/yyyy", "M/d/yyyy" })
                {
                    yield return EmbeddedDate.Replace(value.Value, match =>
                        DateOnly.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                            ? date.ToString(format, CultureInfo.InvariantCulture)
                            : match.Value);
                }
            }
            else if (value.Kind == "date")
            {
                var date = DateOnly.ParseExact(value.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                yield return date.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
                yield return date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                yield return date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            }
            else if (value.Kind == "timestamp")
            {
                var instant = DateTimeOffset.Parse(value.Value, CultureInfo.InvariantCulture);
                foreach (var displayed in new[] { instant.ToUniversalTime(), TimeZoneInfo.ConvertTime(instant, timeZone ?? TimeZoneInfo.Utc) })
                {
                    var zoneSuffix = displayed.Offset == TimeSpan.Zero ? " UTC" : string.Empty;
                    yield return displayed.ToString("MM/dd/yyyy 'at' HH:mm:ss", CultureInfo.InvariantCulture) + zoneSuffix;
                    yield return displayed.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + zoneSuffix;
                    yield return displayed.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                    yield return displayed.ToString("MM/dd/yyyy, HH:mm:ss", CultureInfo.InvariantCulture);
                    yield return displayed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + zoneSuffix;
                    yield return displayed.ToString("MM/dd/yyyy hh:mm:ss tt", CultureInfo.InvariantCulture);
                    yield return displayed.ToString("MM/dd/yyyy, hh:mm:ss tt", CultureInfo.InvariantCulture);
                    yield return displayed.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
                }
            }
        }
    }

    private static string FormatBytes(string value)
        => long.Parse(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture) + " bytes";

    private JsonNode? Visit(JsonNode? node, string? field)
    {
        if (field is "nextCursor" or "criteria" or "parameters" or "filters")
            return node;

        if (node is JsonObject record)
        {
            foreach (var property in record.ToArray())
            {
                var replacement = Visit(property.Value, property.Key);
                if (!ReferenceEquals(property.Value, replacement))
                    record[property.Key] = replacement;
            }

            return record;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var replacement = Visit(array[index], field);
                if (!ReferenceEquals(array[index], replacement))
                    array[index] = replacement;
            }

            return array;
        }

        if (node is not JsonValue scalar)
            return node;

        string? kind = null;
        string? value = null;

        if (scalar.TryGetValue<string>(out var text) && text.Length <= 8000)
        {
            value = text;
            if (field is "fileName" or "text" or "memo")
            {
                kind = "text";
            }
            else if (text.Length == 10
                && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                kind = "date";
            }
            else if (text.Length > 19
                && text[10] == 'T'
                && (text.EndsWith('Z') || text.LastIndexOfAny(['+', '-']) > 18)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                kind = "timestamp";
            }
            else if (EmbeddedDate.IsMatch(text))
            {
                // A date embedded in a description or identifier is literal content,
                // regardless of the field name chosen by the installed application.
                kind = "text";
            }
        }
        else if (field == "sizeBytes" && scalar.TryGetValue<long>(out var size) && size >= 0)
        {
            value = size.ToString(CultureInfo.InvariantCulture);
            kind = "bytes";
        }

        if (kind is null || value is null)
            return node;

        var key = (kind, value);
        if (!_values.TryGetValue(key, out var item))
        {
            if (_values.Count >= MaximumValues)
                return node;

            item = new AiAssistantValue($"{{{{ngb-value:{_prefix}-{_values.Count}}}}}", kind, value);
            _values.Add(key, item);
        }

        // Keep literal content readable in the tool's original string fields.
        // Bind exact copies after generation without obscuring note text behind tokens.
        if (kind == "text")
            return node;

        // Supply the exact size and its label so models do not mistake a display
        // reference for missing metadata or need to calculate a human-readable size.
        if (kind == "bytes")
            return new JsonObject { ["raw"] = node.DeepClone(), ["display"] = item.Reference, ["formatted"] = FormatBytes(value) };

        if (kind == "timestamp")
        {
            var instant = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            var local = TimeZoneInfo.ConvertTime(instant, timeZone ?? TimeZoneInfo.Utc);

            return new JsonObject
            {
                ["display"] = item.Reference,
                ["formatted"] = local.ToString("MM/dd/yyyy, hh:mm:ss tt", CultureInfo.InvariantCulture),
                ["displayTimeZone"] = (timeZone ?? TimeZoneInfo.Utc).Id,
                ["displayUsage"] = "Copy display verbatim for the full date and time. Do not shorten or convert raw; use it only in tool arguments.",
                ["raw"] = node.DeepClone()
            };
        }

        // Raw values remain available for subsequent tool arguments. The display reference
        // is resolved only from the event sent by this request, never from model content.
        return new JsonObject { ["raw"] = node.DeepClone(), ["display"] = item.Reference };
    }
}
