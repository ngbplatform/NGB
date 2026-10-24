namespace NGB.Contracts.Common;

/// <summary>Canonical source kind codes shared by search, MCP and Assistant clients.</summary>
public static class NgbSourceKinds
{
    public const string Catalog = "catalog";
    public const string Document = "document";
    public const string GeneralJournalEntry = "generalJournalEntry";
    public const string Report = "report";

    public static bool IsSupported(string? kind) => IsRecord(kind) || kind == Report;

    public static bool IsRecord(string? kind) => kind is Catalog or Document or GeneralJournalEntry;
}
