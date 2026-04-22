using System.Text;

namespace NGB.Runtime.Catalogs.Schema;

public sealed class SchemaDiagnosticsResult
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;

    public bool HasErrors => _errors.Count > 0;

    public void AddError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _errors.Add(message);
    }

    public void AddWarning(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _warnings.Add(message);
    }

    /// <summary>
    /// A single-line representation suitable for exception messages.
    /// <para>
    /// Note: FluentAssertions' wildcard message matching does not match across newlines,
    /// so exception messages should avoid line breaks.
    /// </para>
    /// </summary>
    public string ToSingleLineString()
    {
        var parts = new List<string>(2);

        if (_errors.Count > 0)
            parts.Add("Errors: " + string.Join("; ", _errors));

        if (_warnings.Count > 0)
            parts.Add("Warnings: " + string.Join("; ", _warnings));

        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        
        if (_errors.Count > 0)
        {
            sb.AppendLine("Errors:");
            foreach (var e in _errors)
            {
                sb.AppendLine("- " + e);
            }
        }

        if (_warnings.Count > 0)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            
            sb.AppendLine("Warnings:");

            foreach (var w in _warnings)
            {
                sb.AppendLine("- " + w);
            }
        }

        return sb.ToString();
    }
}
