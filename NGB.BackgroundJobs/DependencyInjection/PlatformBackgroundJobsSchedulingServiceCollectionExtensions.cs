using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.Configuration;
using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.DependencyInjection;

public static class PlatformBackgroundJobsSchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Binds schedules from configuration (appsettings.json) and registers
    /// <see cref="IJobScheduleProvider"/>.
    ///
    /// Recommended usage (vertical app):
    /// <code>
    /// services.AddPlatformBackgroundJobSchedulesFromConfiguration(configuration);
    /// services.AddPlatformBackgroundJobsHangfire(...);
    /// </code>
    /// </summary>
    public static IServiceCollection AddPlatformBackgroundJobSchedulesFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "BackgroundJobs")
    {
        services.Configure<BackgroundJobsSchedulesOptions>(configuration.GetSection(sectionName));

        // Overrides NullJobScheduleProvider if registered before AddPlatformBackgroundJobsHangfire.
        services.AddSingleton<IJobScheduleProvider, ConfigurationJobScheduleProvider>();

        return services;
    }
}
