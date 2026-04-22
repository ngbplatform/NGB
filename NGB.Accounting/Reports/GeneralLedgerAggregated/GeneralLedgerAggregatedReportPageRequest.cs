using NGB.Core.Base.Paging;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

public sealed class GeneralLedgerAggregatedReportPageRequest : PageSizeBase
{
    public Guid AccountId { get; init; }
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    /// <summary>
    /// Multi-value dimension scopes.
    /// Semantics:
    /// - OR within one dimension scope.
    /// - AND across different dimension scopes.
    /// - hierarchical expansion is resolved before the request reaches the report service.
    /// </summary>
    public DimensionScopeBag? DimensionScopes { get; init; }

    public GeneralLedgerAggregatedReportCursor? Cursor { get; init; }
}
