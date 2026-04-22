using NGB.Core.Dimensions;

namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// A read-side row for a monthly projection (turnovers or balances) of an Operational Register.
///
/// Notes:
/// - Values are keyed by resource <c>column_code</c> (see <see cref="OperationalRegisterResource"/>).
/// - Enrichment fields (<see cref="Dimensions"/> and <see cref="DimensionValueDisplays"/>) are optional
///   and may be populated by readers/services to make UI rendering cheaper.
/// </summary>
public sealed class OperationalRegisterMonthlyProjectionReadRow
{
    public DateOnly PeriodMonth { get; init; }

    public Guid DimensionSetId { get; init; }

    public DimensionBag Dimensions { get; set; } = DimensionBag.Empty;

    /// <summary>
    /// Optional enrichment for UI/report rendering: DimensionId -> display string for the selected ValueId.
    /// (ValueId itself is available via <see cref="Dimensions"/>.)
    /// </summary>
    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; set; }
        = new Dictionary<Guid, string>();

    public IReadOnlyDictionary<string, decimal> Values { get; init; }
        = new Dictionary<string, decimal>(StringComparer.Ordinal);
}
