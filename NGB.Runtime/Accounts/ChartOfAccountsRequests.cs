using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;

namespace NGB.Runtime.Accounts;

public sealed record AccountDimensionRuleRequest(
    string DimensionCode,
    bool IsRequired = false,
    int? Ordinal = null);

public sealed record CreateAccountRequest(
    string Code,
    string Name,
    AccountType Type,
    StatementSection? StatementSection = null,
    bool IsContra = false,
    NegativeBalancePolicy? NegativeBalancePolicy = null,
    bool IsActive = true,
    IReadOnlyList<AccountDimensionRuleRequest>? DimensionRules = null,
    CashFlowRole? CashFlowRole = null,
    string? CashFlowLineCode = null);

public sealed record UpdateAccountRequest(
    Guid AccountId,
    string? Code = null,
    string? Name = null,
    AccountType? Type = null,
    StatementSection? StatementSection = null,
    bool? IsContra = null,
    NegativeBalancePolicy? NegativeBalancePolicy = null,
    bool? IsActive = null,
    IReadOnlyList<AccountDimensionRuleRequest>? DimensionRules = null,
    CashFlowRole? CashFlowRole = null,
    string? CashFlowLineCode = null);
