using System.Text.RegularExpressions;

namespace NGB.Api.GlobalErrorHandling;

internal static partial class ValidationIssueBuilder
{
    public const string FormPath = "_form";

    public static IReadOnlyDictionary<string, string[]>? NormalizeErrors(IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
            return null;

        var normalized = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (rawPath, messages) in errors)
        {
            var path = NormalizePath(rawPath);
            if (!normalized.TryGetValue(path, out var bucket))
            {
                bucket = [];
                normalized[path] = bucket;
            }

            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                if (!bucket.Any(existing => string.Equals(existing, message, StringComparison.Ordinal)))
                    bucket.Add(message);
            }
        }

        return normalized.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.Ordinal);
    }

    public static IReadOnlyList<NgbProblemValidationIssue>? BuildIssues(IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
            return null;

        var issues = new List<NgbProblemValidationIssue>();
        foreach (var (path, messages) in errors)
        {
            var normalizedPath = NormalizePath(path);
            var scope = InferScope(normalizedPath);
            
            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                issues.Add(new NgbProblemValidationIssue(normalizedPath, message, scope));
            }
        }

        return issues.Count == 0 ? null : issues;
    }

    public static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return FormPath;

        var path = rawPath.Trim();
        if (string.Equals(path, FormPath, StringComparison.Ordinal))
            return FormPath;

        if (path.StartsWith("$", StringComparison.Ordinal))
            path = path.TrimStart('$');

        if (path.StartsWith(".", StringComparison.Ordinal))
            path = path[1..];

        path = StripPrefix(path, "request.");
        path = StripPrefix(path, "payload.");
        path = StripPrefix(path, "fields.");

        path = PartRowFieldRegex().Replace(path, match =>
            $"{match.Groups["part"].Value}[{match.Groups["index"].Value}].{match.Groups["field"].Value}");

        path = PartRowRegex().Replace(path, match =>
            $"{match.Groups["part"].Value}[{match.Groups["index"].Value}]");

        path = StripPrefix(path, "parts.");
        path = RowsRegex().Replace(path, "[$1]");
        path = WildcardRowsRegex().Replace(path, "[]");

        path = path.Trim('.');

        return string.IsNullOrWhiteSpace(path) || string.Equals(path, "payload", StringComparison.OrdinalIgnoreCase)
            ? FormPath
            : path;
    }

    public static string InferScope(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, FormPath, StringComparison.Ordinal))
            return "form";

        if (RowRegex().IsMatch(path))
            return "row";

        if (CollectionRegex().IsMatch(path))
            return "collection";

        return "field";
    }

    private static string StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;

    [GeneratedRegex(@"^(?<part>[^.]+)\.rows\[(?<index>\d+)\]\.(?<field>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRowFieldRegex();

    [GeneratedRegex(@"^(?<part>[^.]+)\.rows\[(?<index>\d+)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRowRegex();

    [GeneratedRegex(@"\.rows\[(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RowsRegex();

    [GeneratedRegex(@"\.rows\[\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WildcardRowsRegex();

    [GeneratedRegex(@"\[(?:\d+)?\]$", RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"^[^.]+\[\]$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionRegex();
}
