namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Keyset cursor for movements paging.
/// </summary>
public sealed record OperationalRegisterMovementsPageCursor(long AfterMovementId);
