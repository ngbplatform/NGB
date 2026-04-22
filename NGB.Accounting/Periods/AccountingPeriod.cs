namespace NGB.Accounting.Periods;

public static class AccountingPeriod
{
    /// <summary>
    /// Normalizes the transaction date to the closing period (month)
    /// Example: 2025-12-31 -> 2025-12-01
    /// </summary>
    public static DateOnly FromDateTime(DateTime dt) => new(dt.Year, dt.Month, 1);

    /// <summary>
    /// Normalizes a DateOnly value to the closing period (month)
    /// Example: 2025-12-15 -> 2025-12-01
    /// </summary>
    public static DateOnly FromDateOnly(DateOnly d) => new(d.Year, d.Month, 1);
}
