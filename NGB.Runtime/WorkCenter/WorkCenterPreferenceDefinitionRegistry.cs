using NGB.Application.Abstractions.Services;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.WorkCenter;

public sealed class WorkCenterPreferenceDefinitionRegistry
{
    private readonly IReadOnlyDictionary<string, WorkCenterPreferenceDefinition> _definitions;

    public WorkCenterPreferenceDefinitionRegistry(IEnumerable<IWorkCenterPreferenceDefinitionSource> sources)
    {
        var definitions = new Dictionary<string, WorkCenterPreferenceDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in sources.SelectMany(static x => x.GetDefinitions()))
        {
            Validate(definition);

            if (!definitions.TryAdd(definition.Code, definition))
                throw new NgbConfigurationViolationException($"Work Center preference definition '{definition.Code}' is registered more than once.");
        }

        _definitions = definitions;
    }

    public IReadOnlyList<WorkCenterPreferenceDefinition> All
        => _definitions.Values
            .OrderBy(static x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public WorkCenterPreferenceDefinition Get(string code)
        => _definitions.TryGetValue(code, out var definition)
            ? definition
            : throw new NgbConfigurationViolationException($"Work Center preference definition '{code}' is not registered.");

    private static void Validate(WorkCenterPreferenceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Code) || definition.Code != definition.Code.Trim().ToLowerInvariant())
            throw new NgbConfigurationViolationException("Work Center preference codes must be canonical lowercase values.");

        if (string.IsNullOrWhiteSpace(definition.DisplayName) || string.IsNullOrWhiteSpace(definition.Category))
            throw new NgbConfigurationViolationException($"Work Center preference definition '{definition.Code}' must define display name and category.");

        if (!definition.SupportedChannels.Contains(Core.WorkCenter.NotificationChannel.InApp))
            throw new NgbConfigurationViolationException($"Work Center preference definition '{definition.Code}' must support the in-app channel.");

        if (definition is { IsMandatory: true, UserCanDisable: true })
            throw new NgbConfigurationViolationException($"Mandatory Work Center preference '{definition.Code}' cannot be user-disableable.");
        
        if (definition.ApplicableRoleCodes is { Count: > 0 }
            && definition.ApplicableRoleCodes.Any(static code => string.IsNullOrWhiteSpace(code)
                || !string.Equals(code, code.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
        {
            throw new NgbConfigurationViolationException($"Work Center preference definition '{definition.Code}' contains a non-canonical role code.");
        }
    }
}
