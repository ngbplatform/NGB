using NGB.Accounting.Reports;
using NGB.Accounting.Reports.AccountCard;

namespace NGB.Persistence.Readers.Reports;

/// <summary>Reads a complete ordered range in bounded batches within the caller's read session.</summary>
public interface IAccountCardEffectiveStreamReader
{
    IAsyncEnumerable<IReadOnlyList<AccountCardLine>> ReadAsync(
        AccountActivityQuery query,
        CancellationToken ct = default);
}
