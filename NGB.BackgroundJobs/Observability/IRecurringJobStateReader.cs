namespace NGB.BackgroundJobs.Observability;

internal interface IRecurringJobStateReader
{
    ValueTask<RecurringJobState?> TryGetAsync(string jobId, CancellationToken cancellationToken);
}
