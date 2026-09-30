namespace NGB.OperationalRegisters.Contracts;

public sealed record OperationalRegisterOccurredAtCursor(DateTime AfterOccurredAtUtc, long AfterMovementId);
