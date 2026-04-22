using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Configuration;

namespace NGB.BackgroundJobs.Tests;

public sealed class ConfigurationJobScheduleProvider_P0Tests
{
    [Fact]
    public void GetSchedule_ReturnsNull_WhenGlobalDisabled()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = false,
            NightlyCron = "0 2 * * *"
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        provider.GetSchedule(PlatformJobCatalog.PlatformSchemaValidate).Should().BeNull();
    }

    [Fact]
    public void GetSchedule_UsesNightlyDefault_WhenJobNotExplicitlyConfigured()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = true,
            DefaultTimeZoneId = "UTC",
            NightlyCron = " 0 2 * * *  "
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        var schedule = provider.GetSchedule(PlatformJobCatalog.PlatformSchemaValidate);
        schedule.Should().NotBeNull();
        schedule!.Enabled.Should().BeTrue();
        schedule.JobId.Should().Be(PlatformJobCatalog.PlatformSchemaValidate);
        schedule.Cron.Should().Be("0 2 * * *");
        schedule.TimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public void GetSchedule_ReturnsNull_WhenJobIsExcludedFromNightlyDefault()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = true,
            DefaultTimeZoneId = "UTC",
            NightlyCron = "0 2 * * *",
            NightlyExcludedJobIds = [PlatformJobCatalog.PlatformSchemaValidate]
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        provider.GetSchedule(PlatformJobCatalog.PlatformSchemaValidate).Should().BeNull();
    }

    [Fact]
    public void GetSchedule_PerJobOverride_TakesPrecedence()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = true,
            DefaultTimeZoneId = "UTC",
            NightlyCron = "0 2 * * *"
        };

        options.Jobs[PlatformJobCatalog.PlatformSchemaValidate] = new JobScheduleOptions
        {
            Cron = "*/15 * * * *",
            Enabled = true,
            TimeZoneId = "UTC"
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        var schedule = provider.GetSchedule(PlatformJobCatalog.PlatformSchemaValidate);
        schedule.Should().NotBeNull();
        schedule!.Cron.Should().Be("*/15 * * * *");
    }

    [Fact]
    public void GetSchedule_PerJobDisabled_WinsOverNightlyDefault()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = true,
            DefaultTimeZoneId = "UTC",
            NightlyCron = "0 2 * * *"
        };

        options.Jobs[PlatformJobCatalog.PlatformSchemaValidate] = new JobScheduleOptions
        {
            Cron = null,
            Enabled = false
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        provider.GetSchedule(PlatformJobCatalog.PlatformSchemaValidate).Should().BeNull();
    }

    [Fact]
    public void GetSchedule_DefaultNightlyPolicy_DoesNotSchedule_AutoReversePostingJob()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            Enabled = true,
            DefaultTimeZoneId = "UTC",
            NightlyCron = "0 2 * * *"
        };

        var provider = new ConfigurationJobScheduleProvider(Options.Create(options), NullLogger<ConfigurationJobScheduleProvider>.Instance);

        provider.GetSchedule(PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue).Should().BeNull();
    }
}
