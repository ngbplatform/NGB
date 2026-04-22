using NGB.Tools.Exceptions;

namespace NGB.Accounting.Dimensions;

public sealed record AccountDimensionRule
{
    public AccountDimensionRule(Guid dimensionId, string dimensionCode, int ordinal, bool isRequired)
    {
        if (dimensionId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(dimensionId), "DimensionId must be non-empty");

        if (string.IsNullOrWhiteSpace(dimensionCode))
            throw new NgbArgumentRequiredException(nameof(dimensionCode));

        if (ordinal <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(ordinal), ordinal, "Ordinal must be positive");

        DimensionId = dimensionId;
        DimensionCode = dimensionCode;
        Ordinal = ordinal;
        IsRequired = isRequired;
    }

    public Guid DimensionId { get; }

    public string DimensionCode { get; }

    public int Ordinal { get; }

    public bool IsRequired { get; }
}
