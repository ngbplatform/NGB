namespace NGB.BackgroundJobs.Observability;

internal interface IRecurringJobStateReader
{
    ValueTask<IReadOnlyDictionary<string, RecurringJobState>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken);
}
