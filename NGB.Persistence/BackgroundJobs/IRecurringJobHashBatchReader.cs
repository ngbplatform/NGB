namespace NGB.Persistence.BackgroundJobs;

/// <summary>
/// Storage request for reading recurring-job hashes in one provider round-trip.
/// Connection details are supplied by the scheduler composition root because its
/// database can differ from the application database.
/// </summary>
public sealed record RecurringJobHashBatchRequest(
    string ConnectionString,
    string StorageNamespace,
    IReadOnlyCollection<string> JobIds);

public interface IRecurringJobHashBatchReader
{
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetManyAsync(
        RecurringJobHashBatchRequest request,
        CancellationToken ct = default);
}
