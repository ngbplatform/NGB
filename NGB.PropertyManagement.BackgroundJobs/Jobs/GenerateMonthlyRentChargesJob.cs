using NGB.BackgroundJobs.Contracts;
using NGB.PropertyManagement.BackgroundJobs.Catalog;
using NGB.PropertyManagement.BackgroundJobs.Services;

namespace NGB.PropertyManagement.BackgroundJobs.Jobs;

internal sealed class GenerateMonthlyRentChargesJob(GenerateMonthlyRentChargesService service)
    : IPlatformBackgroundJob
{
    public string JobId => PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges;

    public Task RunAsync(CancellationToken ct)
        => service.ExecuteAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);
}
