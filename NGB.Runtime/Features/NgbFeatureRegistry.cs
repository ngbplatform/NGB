using NGB.Tools.Exceptions;

namespace NGB.Runtime.Features;

public sealed class NgbFeatureRegistry
{
    private readonly IReadOnlyDictionary<string, NgbFeatureDefinition> _byCode;

    public NgbFeatureRegistry(IEnumerable<NgbFeatureDefinition> definitions)
    {
        var byCode = new Dictionary<string, NgbFeatureDefinition>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Code)
                || !definition.Code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                || string.IsNullOrWhiteSpace(definition.DisplayName)
                || string.IsNullOrWhiteSpace(definition.Group)
                || byCode.Keys.Any(code => string.Equals(code, definition.Code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NgbConfigurationViolationException("Feature definitions require unique codes, display names and groups.");
            }

            byCode.Add(definition.Code, definition);
        }

        _byCode = byCode;
        Definitions = Array.AsReadOnly(byCode.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<NgbFeatureDefinition> Definitions { get; }

    public bool Contains(string code) => _byCode.ContainsKey(code);
}
