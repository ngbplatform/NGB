namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterMonthlyProjectionPage(
    Guid RegisterId,
    DateOnly FromInclusive,
    DateOnly ToInclusive,
    IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow> Lines,
    bool HasMore,
    OperationalRegisterMonthlyProjectionPageCursor? NextCursor);
