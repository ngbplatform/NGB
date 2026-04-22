namespace NGB.Metadata.Documents.Hybrid;

public sealed record DocumentIndexMetadata(
    string Name,
    IReadOnlyList<string> ColumnNames,
    bool Unique = false);
