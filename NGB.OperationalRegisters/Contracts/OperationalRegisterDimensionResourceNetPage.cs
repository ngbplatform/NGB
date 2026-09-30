namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterDimensionResourceNetPage(
    IReadOnlyList<OperationalRegisterDimensionResourceNetRow> Rows,
    int Total,
    decimal TotalPositive,
    decimal TotalNegativeAbsolute,
    bool HasMore = false);

public sealed record OperationalRegisterDimensionResourceNetCursor(
    bool AfterPositiveGroup,
    Guid AfterValueId,
    int NextOffset,
    int Total,
    decimal TotalPositive,
    decimal TotalNegativeAbsolute);
