namespace NGB.Runtime.Documents.Schema;

public sealed record SchemaDiagnosticsResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;
}
