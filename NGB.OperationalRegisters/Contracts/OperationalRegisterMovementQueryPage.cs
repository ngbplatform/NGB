namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterMovementQueryPage(
    IReadOnlyList<OperationalRegisterMovementQueryReadRow> Rows,
    long Total);
