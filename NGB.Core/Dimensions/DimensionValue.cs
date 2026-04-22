using NGB.Tools.Exceptions;

namespace NGB.Core.Dimensions;

/// <summary>
/// A single dimension assignment: (DimensionId -> ValueId).
///
/// DimensionId identifies the dimension (e.g., Department, Project).
/// ValueId identifies the selected value (typically a catalog entity ID).
/// </summary>
public readonly record struct DimensionValue
{
    public Guid DimensionId { get; }
    public Guid ValueId { get; }

    public DimensionValue(Guid dimensionId, Guid valueId)
    {
        if (dimensionId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(dimensionId), "DimensionId must not be empty.");

        if (valueId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(valueId), "ValueId must not be empty.");

        DimensionId = dimensionId;
        ValueId = valueId;
    }
}
