namespace NGB.PropertyManagement.BackgroundJobs.Catalog;

public static class PropertyManagementBackgroundJobCatalog
{
    public const string GenerateMonthlyRentCharges = "pm.rent_charge.generate_monthly";

    public static readonly IReadOnlyList<string> All =
    [
        GenerateMonthlyRentCharges
    ];
}
