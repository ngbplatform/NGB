using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Moq;
using NGB.BackgroundJobs.Catalog;
using Xunit;

namespace NGB.CRM.BackgroundJobs.Tests;

public sealed class CrmBackgroundJobsFullCoverageTests
{
    private const string FixedExceptionMarker =
        "Document type 'crm.lead_intake': table 'doc_crm_lead_intake' missing index 'ix_doc_crm_lead_intake__email'";

    [Fact]
    public void CatalogContributor_ReturnsOnlySupportedCrmJobs()
    {
        var jobIds = new CrmBackgroundJobCatalogContributor().GetJobIds();

        jobIds.Should().Equal(
            PlatformJobCatalog.PlatformSchemaValidate,
            PlatformJobCatalog.AuditHealth,
            PlatformJobCatalog.ReferenceRegistersEnsureSchema);
    }

    [Fact]
    public async Task HostedService_RemovesObsoleteJobsAndOnlyMatchingKnownFailure()
    {
        var recurringJobs = new Mock<IRecurringJobManager>(MockBehavior.Strict);
        recurringJobs.Setup(x => x.RemoveIfExists(It.IsAny<string>()));

        var backgroundJobs = new Mock<IBackgroundJobClient>(MockBehavior.Strict);
        backgroundJobs.Setup(x => x.ChangeState(
                "fixed-job",
                It.Is<DeletedState>(state => state != null),
                "Failed"))
            .Returns(true);

        var monitoring = new Mock<IMonitoringApi>(MockBehavior.Strict);
        monitoring.Setup(x => x.FailedJobs(0, 100)).Returns(new JobList<FailedJobDto>(
        [
            new KeyValuePair<string, FailedJobDto>("fixed-job", new FailedJobDto
            {
                ExceptionDetails = $"prefix {FixedExceptionMarker} suffix"
            }),
            new KeyValuePair<string, FailedJobDto>("different-job", new FailedJobDto
            {
                ExceptionDetails = "different failure"
            }),
            new KeyValuePair<string, FailedJobDto>("no-details-job", new FailedJobDto())
        ]));

        var storage = new Mock<JobStorage>(MockBehavior.Strict);
        storage.Setup(x => x.GetMonitoringApi()).Returns(monitoring.Object);

        var sut = new CrmObsoleteRecurringJobsCleanupHostedService(
            recurringJobs.Object,
            backgroundJobs.Object,
            storage.Object);

        await sut.StartAsync(new CancellationToken(canceled: true));
        await sut.StopAsync(new CancellationToken(canceled: true));

        var expectedObsoleteJobs = new[]
        {
            PlatformJobCatalog.AccountingIntegrityScan,
            PlatformJobCatalog.OperationalRegistersFinalizationRunDirtyMonths,
            PlatformJobCatalog.OperationalRegistersEnsureSchema,
            PlatformJobCatalog.AccountingAggregatesDriftCheck,
            PlatformJobCatalog.AccountingOperationsStuckMonitor,
            PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue
        };

        foreach (var jobId in expectedObsoleteJobs)
            recurringJobs.Verify(x => x.RemoveIfExists(jobId), Times.Once);

        recurringJobs.VerifyNoOtherCalls();
        backgroundJobs.Verify(x => x.ChangeState(
            "fixed-job",
            It.Is<DeletedState>(state => state != null),
            "Failed"), Times.Once);
        backgroundJobs.VerifyNoOtherCalls();
        monitoring.Verify(x => x.FailedJobs(0, 100), Times.Once);
        monitoring.VerifyNoOtherCalls();
        storage.Verify(x => x.GetMonitoringApi(), Times.Once);
        storage.VerifyNoOtherCalls();
    }
}
