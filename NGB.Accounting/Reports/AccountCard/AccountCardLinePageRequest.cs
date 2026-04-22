using NGB.Core.Base.Paging;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.AccountCard;

public sealed class AccountCardLinePageRequest : PageSizeBase
{
    public Guid AccountId { get; init; }
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }
    
    /// <summary>
    /// Multi-value dimension scopes.
    /// Semantics:
    /// - OR within one dimension scope.
    /// - AND across different dimension scopes.
    /// - hierarchical expansion is resolved before the request reaches the low-level reader.
    /// </summary>
    public DimensionScopeBag? DimensionScopes { get; init; }

    public AccountCardLineCursor? Cursor { get; init; }

    /// <summary>
    /// When true, the effective reader should also return grand totals for the whole filtered range
    /// in the page envelope so the caller can cache them in the cursor.
    /// Raw line readers may ignore this flag.
    /// </summary>
    public bool IncludeTotals { get; init; }
}
