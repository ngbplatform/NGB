using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.BackgroundJobs.Observability;
using NGB.Persistence.BackgroundJobs;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.DependencyInjection;

public static class PlatformBackgroundJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers Hangfire as the only scheduler for NGB background jobs.
    ///
    /// The vertical application is expected to provide an <see cref="IJobScheduleProvider"/> implementation
    /// (typically bound from appsettings.json). If none is registered, all jobs remain unscheduled.
    /// </summary>
    public static IServiceCollection AddPlatformBackgroundJobsHangfire(
        this IServiceCollection services,
        JobStorage jobStorage,
        Action<PlatformHangfireOptions> configure)
    {
        if (services is null)
            throw new NgbArgumentRequiredException(nameof(services));

        if (jobStorage is null)
            throw new NgbArgumentRequiredException(nameof(jobStorage));

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        var opts = new PlatformHangfireOptions();
        configure(opts);

        // Expose options via IOptions for downstream services.
        services.TryAddSingleton(Options.Create(opts));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(jobStorage);

        // Default (no scheduling) provider; the app can override by registering its own.
        services.TryAddSingleton<IJobScheduleProvider, NullJobScheduleProvider>();

        // Default (no notifications) notifier; the app can override by registering its own.
        services.TryAddSingleton<IPlatformJobNotifier, NullPlatformJobNotifier>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundJobCatalogContributor, PlatformBackgroundJobCatalogContributor>());
        services.TryAddSingleton<IBackgroundJobCatalog, BackgroundJobCatalog>();

        // Job runner is resolved by Hangfire via DI.
        services.TryAddTransient<PlatformHangfireJobRunner>();

        // Per-run in-process counters used by the runner summary log.
        services.TryAddScoped<IJobRunMetrics, JobRunMetrics>();

        // Health reporter: desired schedules vs actual Hangfire recurring job state.
        services.TryAddSingleton<IRecurringJobStateReader>(sp => new HangfireRecurringJobStateReader(
            sp.GetRequiredService<JobStorage>(),
            sp.GetRequiredService<IOptions<PlatformHangfireOptions>>(),
            sp.GetService<IRecurringJobHashBatchReader>()));
        services.TryAddSingleton<IBackgroundJobsHealthReporter, BackgroundJobsHealthReporter>();

        // Default platform job implementations (catalog).
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, PlatformSchemaValidateJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, AccountingIntegrityScanJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, AuditHealthJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, OperationalRegistersFinalizeDirtyMonthsJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, OperationalRegistersEnsureSchemaJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, ReferenceRegistersEnsureSchemaJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, AccountingAggregatesDriftCheckJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, AccountingOperationsStuckMonitorJob>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPlatformBackgroundJob, GeneralJournalEntryAutoReversePostingJob>());

        services.AddHangfire((sp, config) =>
        {
            config
                .UseSimpleAssemblyNameTypeSerializer()
                .UseStorage(sp.GetRequiredService<JobStorage>());
        });

        services.AddHangfireServer(serverOptions =>
        {
            serverOptions.WorkerCount = opts.WorkerCount;
            if (opts.Queues.Length > 0)
                serverOptions.Queues = opts.Queues;
            
            if (!string.IsNullOrWhiteSpace(opts.ServerName))
                serverOptions.ServerName = opts.ServerName;
        });

        // Startup registration of recurring jobs based on catalog
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PlatformHangfireRecurringJobsHostedService>());

        return services;
    }
}
