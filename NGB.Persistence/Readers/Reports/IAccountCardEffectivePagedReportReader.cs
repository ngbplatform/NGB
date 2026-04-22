using NGB.Accounting.Reports.AccountCard;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Builds canonical Account Card pages over the effective stream (not raw posting history).
/// This contract exists specifically for the cursor-paged canonical report path.
/// </summary>
public interface IAccountCardEffectivePagedReportReader
{
    Task<AccountCardReportPage> GetPageAsync(AccountCardReportPageRequest request, CancellationToken ct = default);
}
