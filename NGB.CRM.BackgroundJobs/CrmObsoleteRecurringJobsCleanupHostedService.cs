using Hangfire;
using NGB.BackgroundJobs.Catalog;

namespace NGB.CRM.BackgroundJobs;

internal sealed class CrmObsoleteRecurringJobsCleanupHostedService(
    IRecurringJobManager recurringJobManager,
    IBackgroundJobClient backgroundJobClient,
    JobStorage jobStorage) : IHostedService
{
    private static readonly string[] ObsoleteJobIds =
    [
        PlatformJobCatalog.AccountingIntegrityScan,
        PlatformJobCatalog.OperationalRegistersFinalizationRunDirtyMonths,
        PlatformJobCatalog.OperationalRegistersEnsureSchema,
        PlatformJobCatalog.AccountingAggregatesDriftCheck,
        PlatformJobCatalog.AccountingOperationsStuckMonitor,
        PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var jobId in ObsoleteJobIds)
        {
            recurringJobManager.RemoveIfExists(jobId);
        }

        DeleteFailedJobsForFixedLeadIntakeEmailIndex();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void DeleteFailedJobsForFixedLeadIntakeEmailIndex()
    {
        const string fixedExceptionMarker =
            "Document type 'crm.lead_intake': table 'doc_crm_lead_intake' missing index 'ix_doc_crm_lead_intake__email'";

        var failedJobs = jobStorage.GetMonitoringApi().FailedJobs(0, 100);

        foreach (var (jobId, failedJob) in failedJobs)
        {
            if (failedJob.ExceptionDetails?.Contains(fixedExceptionMarker, StringComparison.Ordinal) == true)
            {
                backgroundJobClient.Delete(jobId, "Failed");
            }
        }
    }
}
