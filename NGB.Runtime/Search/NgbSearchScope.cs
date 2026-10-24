namespace NGB.Runtime.Search;

/// <summary>Selects the business read source used by the shared search pipeline.</summary>
public enum NgbSearchScope
{
    All,
    None,
    Documents,
    Catalogs,
    Reports
}
