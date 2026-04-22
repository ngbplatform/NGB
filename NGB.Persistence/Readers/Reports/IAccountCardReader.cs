using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

public interface IAccountCardReader
{
    /// <summary>
    /// Canonical scope-based API.
    /// Semantics:
    /// - OR within one dimension scope.
    /// - AND across different dimension scopes.
    /// </summary>
    Task<IReadOnlyList<AccountCardLine>> GetAsync(
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes = null,
        CancellationToken ct = default);
}
