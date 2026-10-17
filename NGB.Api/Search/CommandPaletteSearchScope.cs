using NGB.Runtime.Search;

namespace NGB.Api.Search;

/// <summary>Translates command palette syntax into business search scopes.</summary>
public static class CommandPaletteSearchScope
{
    public static NgbSearchScope Parse(string? scope)
        => (scope ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => NgbSearchScope.All,
            ":" or "document" or "documents" => NgbSearchScope.Documents,
            "@" or "catalog" or "catalogs" => NgbSearchScope.Catalogs,
            "#" or "report" or "reports" => NgbSearchScope.Reports,
            _ => NgbSearchScope.None
        };
}
