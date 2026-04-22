namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterMovementsPage(
    Guid RegisterId,
    DateOnly FromInclusive,
    DateOnly ToInclusive,
    IReadOnlyList<OperationalRegisterMovementQueryReadRow> Lines,
    bool HasMore,
    OperationalRegisterMovementsPageCursor? NextCursor);
