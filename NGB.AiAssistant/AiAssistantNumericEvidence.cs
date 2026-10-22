using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NGB.AiAssistant;

/// <summary>Checks decimal precision and currency claims against successful reads, without rewriting an answer.</summary>
internal sealed class AiAssistantNumericEvidence
{
    private static readonly Regex Number = new(
        @"(?<![\w./:-])[+-]?[0-9]+(?:[.,\u00a0\u202f][0-9]+)*(?![\w/:-]|\.\w|,[0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Currency = new(
        @"(?<before>\p{Sc}|\b[A-Z]{3})\s*[+-]?[0-9]|[0-9]\s*(?<after>\p{Sc}|[A-Z]{3}\b)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, string[]> CurrencySymbols = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name))
        .GroupBy(region => region.ISOCurrencySymbol, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group
                .SelectMany(region => region.CurrencySymbol
                    .Where(character => char.GetUnicodeCategory(character) == UnicodeCategory.CurrencySymbol))
                .Select(character => character.ToString())
                .Distinct()
                .ToArray(),
            StringComparer.Ordinal);

    private readonly HashSet<decimal> _numbers = [];
    private readonly HashSet<string> _currencies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _literals = new(StringComparer.Ordinal);

    internal void Add(object? data)
        => Visit(JsonSerializer.SerializeToElement(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    internal string? Correction(string answer)
    {
        if (_numbers.Count == 0)
            return null;

        // Supplied text (identifiers, filenames, notes) and bound display values are
        // literals, not numeric claims. Never reinterpret numbers embedded in them.
        var prose = Regex.Replace(answer, @"\{\{ngb-value:[^{}]+\}\}", " ", RegexOptions.CultureInvariant);
        foreach (var literal in _literals.OrderByDescending(value => value.Length))
        {
            prose = prose.Replace(literal, " ", StringComparison.Ordinal);
        }

        // Currency labels may touch the amount (USD12.50 or 12.50USD). Separate
        // recognized labels before matching numbers, retaining them for validation below.
        var numericProse = Currency.Replace(prose, match =>
        {
            var label = match.Groups["before"].Success
                ? match.Groups["before"].Value
                : match.Groups["after"].Value;

            var replacement = match.Groups["before"].Success && match.Index > 0 && prose[match.Index - 1] is '-' or '+'
                ? ""
                : " ";

            return label.Length == 1 || CurrencySymbols.ContainsKey(label)
                ? match.Value.Replace(label, replacement, StringComparison.Ordinal)
                : match.Value;
        });

        var matches = Number.Matches(numericProse).Cast<Match>().ToArray();
        var exact = matches.SelectMany(match => Parse(match.Value)).Where(_numbers.Contains).ToHashSet();

        var unsupported = matches
            .Where(match => match.Value.Contains('.') || match.Value.Contains(','))
            .Where(match => Parse(match.Value).Any())
            .Where(match => !Parse(match.Value).Any(value => _numbers.Contains(value)
                || (numericProse.AsSpan(match.Index + match.Length).TrimStart().StartsWith("%") && _numbers.Contains(value / 100m))
                // An explicitly rounded presentation can retain its exact source next
                // to it. A rounded number alone must not replace the source value.
                || exact.Any(source => decimal.Round(source, (decimal.GetBits(value)[3] >> 16) & 0xff) == value)))
            .Select(match => match.Value)
            .Distinct()
            .Take(5)
            .ToArray();

        var currencies = Currency.Matches(prose)
            .Select(match => match.Groups["before"].Success ? match.Groups["before"].Value : match.Groups["after"].Value)
            .Where(value => (value.Length == 1 || CurrencySymbols.ContainsKey(value)) && !_currencies.Contains(value))
            .Distinct()
            .Take(5)
            .ToArray();

        if (unsupported.Length == 0 && currencies.Length == 0)
            return null;

        return "The draft failed the numeric evidence check: "
            + JsonSerializer.Serialize(new { unsupportedNumbers = unsupported, unsupportedCurrencies = currencies })
            + ". Answer again from the successful read results. Copy the exact returned numbers; "
            + "do not round them or borrow a currency from a previous answer. Include a currency only when "
            + "the read supplies it. If rounding was explicitly requested, include the exact source value too. "
            + "Do not mention this check or repeat the incorrect draft.";
    }

    private void Visit(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                // Request criteria and schema choices are not returned business values.
                if (property.Name is not ("criteria" or "parameters" or "filters" or "nextCursor" or "options"))
                    Visit(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                Visit(item);
            }
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            _numbers.Add(number);
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
        {
            if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var numericText))
            {
                _numbers.Add(numericText);
            }
            else if (text.Length >= 4 && text.Any(char.IsAsciiDigit))
            {
                _literals.Add(text);
            }

            if (CurrencySymbols.TryGetValue(text, out var symbols))
            {
                _currencies.Add(text);
                _currencies.UnionWith(symbols);
            }
            else if (text.Length == 1 && char.GetUnicodeCategory(text[0]) == UnicodeCategory.CurrencySymbol)
            {
                _currencies.Add(text);
            }
        }
    }

    private static IEnumerable<decimal> Parse(string text)
    {
        // Accept decimal comma and decimal point, with strictly grouped thousands.
        // A single separator followed by three digits is ambiguous; either exact
        // interpretation may be supported by the source, without changing the answer.
        foreach (var separator in new[] { '.', ',' })
        {
            var group = separator == '.' ? ',' : '.';
            var pattern = @"^[+-]?(?:[0-9]+|[0-9]{1,3}(?:[" + Regex.Escape(group.ToString())
                + @"\u00a0\u202f][0-9]{3})+)(?:" + Regex.Escape(separator.ToString()) + @"[0-9]+)?$";

            if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                continue;

            var normalized = text
                .Replace(group.ToString(), "", StringComparison.Ordinal)
                .Replace("\u00a0", "", StringComparison.Ordinal)
                .Replace("\u202f", "", StringComparison.Ordinal)
                .Replace(separator, '.');

            if (decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
            {
                yield return number;
            }
        }
    }
}
