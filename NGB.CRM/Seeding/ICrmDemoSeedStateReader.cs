namespace NGB.CRM.Seeding;

/// <summary>
/// Reads persisted CRM state required to make demo seeding idempotent.
/// </summary>
public interface ICrmDemoSeedStateReader
{
    Task<int> CountLeadIntakesByNamePrefixAsync(string leadNamePrefix, CancellationToken ct = default);
}
