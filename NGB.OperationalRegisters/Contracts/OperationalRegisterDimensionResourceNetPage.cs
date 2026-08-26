namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterDimensionResourceNetPage(
    IReadOnlyList<OperationalRegisterDimensionResourceNetRow> Rows,
    int Total,
    decimal TotalPositive,
    decimal TotalNegativeAbsolute);
