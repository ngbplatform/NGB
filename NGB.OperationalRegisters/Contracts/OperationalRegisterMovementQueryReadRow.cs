using NGB.Core.Dimensions;

namespace NGB.OperationalRegisters.Contracts;

public sealed class OperationalRegisterMovementQueryReadRow
{
    public long MovementId { get; init; }

    public Guid DocumentId { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public DateOnly PeriodMonth { get; init; }

    public Guid DimensionSetId { get; init; }

    public bool IsStorno { get; init; }

    public DimensionBag Dimensions { get; set; } = DimensionBag.Empty;

    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; set; }
        = new Dictionary<Guid, string>();

    public IReadOnlyDictionary<string, decimal> Values { get; init; }
        = new Dictionary<string, decimal>(StringComparer.Ordinal);
}
