using Microsoft.Extensions.Options;

namespace NGB.Mcp;

/// <summary>
/// Deployment budgets for MCP reads. Defaults are conservative starting values;
/// existing application query and paging limits still apply independently.
/// </summary>
public sealed class McpReadOptions
{
    /// <summary>
    /// Supported container depth of the shared MCP serializer and schema exporter.
    /// This is an implementation ceiling, not an MCP protocol requirement.
    /// </summary>
    public const int MaximumSupportedJsonDepth = 32;

    /// <summary>Positive byte budget for a successful tool result, including structured content and JSON text.</summary>
    public int MaxResponseBytes { get; set; } = 1024 * 1024;

    /// <summary>Positive UTF-8 byte budget for tool arguments, excluding the HTTP/JSON-RPC envelope.</summary>
    public int MaxInputBytes { get; set; } = 64 * 1024;

    /// <summary>Positive limit on all registered tools, including built-ins and application contributors.</summary>
    public int MaxTools { get; set; } = 100;

    /// <summary>Maximum argument container depth; the root object counts as one level.</summary>
    public int MaxJsonDepth { get; set; } = MaximumSupportedJsonDepth;

    /// <summary>Positive physical-row budget for a record's base row, typed head and tabular parts.</summary>
    public int MaxRecordRows { get; set; } = 1_000;

    /// <summary>
    /// Positive storage JSON byte budget checked before record materialization.
    /// This differs from the serialized tool result size and is enforced independently.
    /// </summary>
    public int MaxRecordBytes { get; set; } = 256 * 1024;
}

internal sealed class McpReadOptionsValidator : IValidateOptions<McpReadOptions>
{
    public ValidateOptionsResult Validate(string? name, McpReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidatePositive(options.MaxInputBytes, nameof(options.MaxInputBytes), failures);
        ValidatePositive(options.MaxResponseBytes, nameof(options.MaxResponseBytes), failures);
        ValidatePositive(options.MaxRecordRows, nameof(options.MaxRecordRows), failures);
        ValidatePositive(options.MaxRecordBytes, nameof(options.MaxRecordBytes), failures);
        ValidatePositive(options.MaxTools, nameof(options.MaxTools), failures);

        if (options.MaxJsonDepth is < 1 or > McpReadOptions.MaximumSupportedJsonDepth)
        {
            failures.Add($"{nameof(McpReadOptions)}.{nameof(options.MaxJsonDepth)} must be between 1 and "
                + $"{McpReadOptions.MaximumSupportedJsonDepth}, the supported MCP serializer depth.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePositive(int value, string optionName, ICollection<string> failures)
    {
        if (value <= 0)
            failures.Add($"{nameof(McpReadOptions)}.{optionName} must be positive.");
    }
}
