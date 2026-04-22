using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.DependencyInjection;

public sealed class NgbBackgroundJobsServiceCollectionExtensions_P0Tests
{
    [Fact]
    public void AddNgbBackgroundJobsHangfire_WhenConnectionStringMissing_ThrowsConfigurationViolation()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPlatformBackgroundJobsHangfire(o =>
        {
            o.ConnectionString = ""; // missing
        });

        var ex = act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*ConnectionString must be provided*")
            .Which;

        ex.ErrorCode.Should().Be(NgbConfigurationViolationException.Code);
    }

    [Fact]
    public void AddNgbBackgroundJobsHangfire_Registers_Defaults_And_AllPlatformJobs()
    {
        var services = new ServiceCollection();

        services.AddPlatformBackgroundJobsHangfire(o =>
        {
            // We do not build or resolve JobStorage in this unit test; the connection string is only required
            // for registering Hangfire PostgreSQL storage configuration.
            o.ConnectionString = "Host=localhost;Port=5432;Database=ngb_test;Username=ngb;Password=ngb";
            o.PrepareSchemaIfNecessary = false;
            o.WorkerCount = 1;
        });

        // Default schedule provider + notifier must be registered (the vertical app may override).
        var schedule = services.Single(x => x.ServiceType == typeof(IJobScheduleProvider));
        schedule.Lifetime.Should().Be(ServiceLifetime.Singleton);
        schedule.ImplementationType.Should().NotBeNull();
        schedule.ImplementationType!.Name.Should().Be("NullJobScheduleProvider");

        var notifier = services.Single(x => x.ServiceType == typeof(IPlatformJobNotifier));
        notifier.Lifetime.Should().Be(ServiceLifetime.Singleton);
        notifier.ImplementationType.Should().NotBeNull();
        notifier.ImplementationType!.Name.Should().Be("NullPlatformJobNotifier");

        // Startup hosted service for recurring jobs must be present.
        services.Should().Contain(x =>
            x.ServiceType == typeof(IHostedService) &&
            x.ImplementationType == typeof(PlatformHangfireRecurringJobsHostedService));

        // Platform job implementations must be registered (catalog contract).
        var jobImpls = services
            .Where(x => x.ServiceType == typeof(IPlatformBackgroundJob))
            .Select(x => x.ImplementationType)
            .ToList();

        jobImpls.Should().Contain(typeof(PlatformSchemaValidateJob));
        jobImpls.Should().Contain(typeof(AccountingIntegrityScanJob));
        jobImpls.Should().Contain(typeof(AuditHealthJob));
        jobImpls.Should().Contain(typeof(OperationalRegistersFinalizeDirtyMonthsJob));
        jobImpls.Should().Contain(typeof(OperationalRegistersEnsureSchemaJob));
        jobImpls.Should().Contain(typeof(ReferenceRegistersEnsureSchemaJob));
        jobImpls.Should().Contain(typeof(AccountingAggregatesDriftCheckJob));
        jobImpls.Should().Contain(typeof(AccountingOperationsStuckMonitorJob));
        jobImpls.Should().Contain(typeof(GeneralJournalEntryAutoReversePostingJob));
    }
}
