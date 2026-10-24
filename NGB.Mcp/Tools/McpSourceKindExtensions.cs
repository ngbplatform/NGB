using System.Collections.Frozen;
using NGB.Contracts.Common;

namespace NGB.Mcp.Tools;

public static class McpSourceKindExtensions
{
    private static readonly FrozenDictionary<string, McpSourceKind> KindsByCode = Enum.GetValues<McpSourceKind>()
        .ToFrozenDictionary(kind => kind.ToCode(), StringComparer.Ordinal);

    public static string ToCode(this McpSourceKind kind)
        => kind switch
        {
            McpSourceKind.Catalog => NgbSourceKinds.Catalog,
            McpSourceKind.Document => NgbSourceKinds.Document,
            McpSourceKind.GeneralJournalEntry => NgbSourceKinds.GeneralJournalEntry,
            McpSourceKind.Report => NgbSourceKinds.Report,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported source kind.")
        };

    public static bool TryFromCode(string? code, out McpSourceKind kind)
    {
        kind = default;
        return code is not null && KindsByCode.TryGetValue(code, out kind);
    }
}
