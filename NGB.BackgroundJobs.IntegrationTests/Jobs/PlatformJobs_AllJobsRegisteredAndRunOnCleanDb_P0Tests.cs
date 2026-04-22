using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

/// <summary>
/// Smoke/contract: on a clean platform schema, ALL platform jobs must be:
/// 1) registered in DI (no missing implementations), and
/// 2) runnable without throwing (no hidden runtime contract drift).
///
/// This is intentionally end-to-end: it composes platform Runtime + PostgreSQL + BackgroundJobs.
/// </summary>
[Collection(HangfirePostgresCollection.Name)]
public sealed class PlatformJobs_AllJobsRegisteredAndRunOnCleanDb_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task AllJobs_AreRegistered_And_RunOnCleanDatabase()
    {
        using var sp = BuildServiceProvider(fixture.ConnectionString);

        // 1) Registration contract: every PlatformJobCatalog id must have exactly one implementation.
        await using (var scope = sp.CreateAsyncScope())
        {
            var jobs = scope.ServiceProvider.GetServices<IPlatformBackgroundJob>().ToList();

            jobs.Should().NotBeEmpty();
            jobs.Select(x => x.JobId).Should().OnlyHaveUniqueItems();
            jobs.Select(x => x.JobId).Should().BeEquivalentTo(PlatformJobCatalog.All);
        }

        // 2) Runtime contract: each job must run on a clean database.
        // Run each in its own scope to ensure scoped dependencies (UnitOfWork, IJobRunMetrics) are fresh.
        foreach (var jobId in PlatformJobCatalog.All)
        {
            await using var scope = sp.CreateAsyncScope();

            var job = scope.ServiceProvider
                .GetServices<IPlatformBackgroundJob>()
                .Single(x => x.JobId == jobId);

            await job.RunAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();

        // Note: we intentionally compose the full BackgroundJobs module to catch DI regressions.
        // We do not start a Host here, so Hangfire Server hosted service will not run.
        services.AddPlatformBackgroundJobsHangfire(o =>
        {
            o.ConnectionString = connectionString;
            o.PrepareSchemaIfNecessary = true;
            o.WorkerCount = 1;
        });

        return services.BuildServiceProvider();
    }
}
