namespace NGB.Accounting.Balances;

/// <summary>
/// Balance/turnover key used by operational balance enforcement and month closing.
///
/// IMPORTANT:
/// This key is based on Dimension Set ID (open-ended analytical dimensions).
/// Guid.Empty is reserved for the empty set.
/// </summary>
public readonly record struct AccountingBalanceKey(Guid AccountId, Guid DimensionSetId);
