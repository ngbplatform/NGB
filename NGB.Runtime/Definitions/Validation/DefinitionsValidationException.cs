using NGB.Tools.Exceptions;

namespace NGB.Runtime.Definitions.Validation;

/// <summary>
/// Thrown when module-provided Definitions are inconsistent with runtime invariants
/// (wrong interfaces, missing DI registrations, metadata/type mismatches).
/// </summary>
public sealed class DefinitionsValidationException(IReadOnlyList<string>? errors) : NgbConfigurationException(
    BuildMessage(errors ?? throw new NgbArgumentRequiredException(nameof(errors))),
    Code,
    CreateContext(errors ?? throw new NgbArgumentRequiredException(nameof(errors))))
{
    public const string Code = "ngb.definitions.validation_failed";

    public IReadOnlyList<string> Errors { get; } = errors ?? throw new NgbArgumentRequiredException(nameof(errors));

    private static IReadOnlyDictionary<string, object?> CreateContext(IReadOnlyList<string> errors)
    {
        return new Dictionary<string, object?>
        {
            ["errorsCount"] = errors.Count,
            ["errors"] = errors
        };
    }

    private static string BuildMessage(IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0)
            return "Definitions validation failed.";

        var lines = new List<string>(errors.Count + 1)
        {
            $"Definitions validation failed: {errors.Count} error(s)."
        };

        foreach (var e in errors)
        {
            lines.Add($"- {e}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
