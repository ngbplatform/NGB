using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Reporting;

public sealed record OccupancySummaryRow(
    Guid BuildingId,
    string BuildingDisplay,
    DateOnly AsOfUtc,
    int TotalUnits,
    int OccupiedUnits)
{
    public int VacantUnits => Math.Max(0, TotalUnits - OccupiedUnits);

    public decimal OccupancyPercent
    {
        get
        {
            if (TotalUnits <= 0)
                return 0m;

            var pct = OccupiedUnits * 100m / TotalUnits;
            return Math.Round(pct, 2, MidpointRounding.AwayFromZero);
        }
    }

    public void EnsureInvariant()
    {
        if (BuildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(BuildingId), "BuildingId must not be empty.");

        if (string.IsNullOrWhiteSpace(BuildingDisplay))
            throw new NgbArgumentInvalidException(nameof(BuildingDisplay), "BuildingDisplay is required.");

        if (TotalUnits < 0 || OccupiedUnits < 0)
            throw new NgbArgumentInvalidException(nameof(TotalUnits), "Counts must not be negative.");

        if (OccupiedUnits > TotalUnits)
            throw new NgbArgumentInvalidException(nameof(OccupiedUnits), "OccupiedUnits must not exceed TotalUnits.");
    }
}

public sealed record OccupancySummaryTotals(
    DateOnly AsOfUtc,
    int BuildingCount,
    int TotalUnits,
    int OccupiedUnits)
{
    public int VacantUnits => Math.Max(0, TotalUnits - OccupiedUnits);

    public decimal OccupancyPercent
    {
        get
        {
            if (TotalUnits <= 0)
                return 0m;

            var pct = OccupiedUnits * 100m / TotalUnits;
            return Math.Round(pct, 2, MidpointRounding.AwayFromZero);
        }
    }

    public void EnsureInvariant()
    {
        if (BuildingCount < 0 || TotalUnits < 0 || OccupiedUnits < 0)
            throw new NgbArgumentInvalidException(nameof(BuildingCount), "Counts must not be negative.");

        if (OccupiedUnits > TotalUnits)
            throw new NgbArgumentInvalidException(nameof(OccupiedUnits), "OccupiedUnits must not exceed TotalUnits.");
    }
}

public sealed record OccupancySummaryPage(
    IReadOnlyList<OccupancySummaryRow> Rows,
    int Total,
    OccupancySummaryTotals Totals)
{
    public void EnsureInvariant()
    {
        if (Rows is null)
            throw new NgbArgumentRequiredException(nameof(Rows));

        if (Total < 0)
            throw new NgbArgumentInvalidException(nameof(Total), "Total must not be negative.");

        Totals.EnsureInvariant();

        foreach (var row in Rows)
            row.EnsureInvariant();
    }
}
