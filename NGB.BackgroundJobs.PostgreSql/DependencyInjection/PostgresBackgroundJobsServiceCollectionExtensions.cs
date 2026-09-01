using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Persistence.BackgroundJobs;

namespace NGB.BackgroundJobs.PostgreSql.DependencyInjection;

public static class PostgresBackgroundJobsServiceCollectionExtensions
{
    public static IServiceCollection AddNgbPostgresBackgroundJobsAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IRecurringJobHashBatchReader, PostgresRecurringJobHashBatchReader>();
        return services;
    }
}
