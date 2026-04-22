using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Reporting;

public sealed record BuildingSummary(
    Guid BuildingId,
    string BuildingDisplay,
    DateOnly AsOfUtc,
    int TotalUnits,
    int OccupiedUnits)
{
    public int VacantUnits => Math.Max(0, TotalUnits - OccupiedUnits);

    public decimal VacancyPercent
    {
        get
        {
            if (TotalUnits <= 0)
                return 0m;

            // Keep the UI stable: round to 2 decimals.
            var pct = VacantUnits * 100m / TotalUnits;
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
    }
}
