namespace NGB.Trade.Runtime.Reporting;

internal static class TradeDashboardLimits
{
    public const int Top = 5;
    public const int Inventory = 8;
    public const int RecentDocuments = 8;
    // Six section headings, four KPIs, three top lists, inventory and recent documents.
    public const int Rows = 6 + 4 + 3 * Top + Inventory + RecentDocuments;
    public const int Columns = 5;
}
