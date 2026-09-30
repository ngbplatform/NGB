using NGB.Accounting.Reports;
using NGB.Accounting.Reports.GeneralLedgerAggregated;

namespace NGB.Persistence.Readers.Reports;

/// <summary>Reads a complete ordered range in bounded batches within the caller's read session.</summary>
public interface IGeneralLedgerAggregatedStreamReader
{
    IAsyncEnumerable<IReadOnlyList<GeneralLedgerAggregatedLine>> ReadAsync(
        AccountActivityQuery query,
        CancellationToken ct = default);
}
