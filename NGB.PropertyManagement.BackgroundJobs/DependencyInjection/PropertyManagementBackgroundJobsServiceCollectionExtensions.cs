using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.PropertyManagement.BackgroundJobs.Catalog;
using NGB.PropertyManagement.BackgroundJobs.Jobs;
using NGB.PropertyManagement.BackgroundJobs.Services;

namespace NGB.PropertyManagement.BackgroundJobs.DependencyInjection;

public static class PropertyManagementBackgroundJobsServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyManagementBackgroundJobsModule(this IServiceCollection services)
    {
        services.TryAddScoped<GenerateMonthlyRentChargesService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundJobCatalogContributor, PropertyManagementBackgroundJobCatalogContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, GenerateMonthlyRentChargesJob>());

        return services;
    }

    private sealed class PropertyManagementBackgroundJobCatalogContributor : IBackgroundJobCatalogContributor
    {
        public IReadOnlyCollection<string> GetJobIds() => PropertyManagementBackgroundJobCatalog.All;
    }
}
