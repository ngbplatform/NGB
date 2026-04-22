using NGB.Accounting.Dimensions;
using NGB.Accounting.CashFlow;
using NGB.Core.Base;
using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

public sealed class Account : Entity
{
    public string Code { get; }
    public string Name { get; }
    public AccountType Type { get; }

    // Reporting metadata.
    public StatementSection StatementSection { get; }
    public bool IsContra { get; }
    public CashFlowRole CashFlowRole { get; }
    public string? CashFlowLineCode { get; }

    // Derived reporting metadata.
    public NormalBalance NormalBalance { get; }

    /// <summary>
    /// Account analytical dimension rules (unlimited).
    /// </summary>
    public IReadOnlyList<AccountDimensionRule> DimensionRules { get; }

    public NegativeBalancePolicy NegativeBalancePolicy { get; }

    public Account(
        Guid? id,
        string code,
        string name,
        AccountType type,
        StatementSection? statementSection = null,
        NegativeBalancePolicy? negativeBalancePolicy = null,
        bool isContra = false,
        IReadOnlyList<AccountDimensionRule>? dimensionRules = null,
        CashFlowRole cashFlowRole = CashFlowRole.None,
        string? cashFlowLineCode = null)
    {
        if (id is not null)
            Id = id.Value;

        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        Type = type;

        StatementSection = statementSection ?? StatementSectionDefaults.FromAccountType(type);

        IsContra = isContra;
        CashFlowRole = cashFlowRole;
        CashFlowLineCode = string.IsNullOrWhiteSpace(cashFlowLineCode) ? null : cashFlowLineCode.Trim();
        NormalBalance = NormalBalanceDefaults
            .FromStatementSection(StatementSection)
            .ApplyContra(IsContra);

        DimensionRules = NormalizeDimensionRules(dimensionRules);

        NegativeBalancePolicy = negativeBalancePolicy ?? type switch
        {
            AccountType.Asset => NegativeBalancePolicy.Warn,
            _ => NegativeBalancePolicy.Allow
        };
    }

    private static IReadOnlyList<AccountDimensionRule> NormalizeDimensionRules(IReadOnlyList<AccountDimensionRule>? rules)
    {
        if (rules is null || rules.Count == 0)
            return [];

        var ordered = rules.OrderBy(x => x.Ordinal).ToArray();

        var duplicatesById = ordered
            .GroupBy(x => x.DimensionId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatesById.Count > 0)
            throw new NgbArgumentInvalidException(
                paramName: "dimensionRules",
                reason: $"Duplicate DimensionId in AccountDimensionRules: {string.Join(", ", duplicatesById)}");

        var duplicatesByOrdinal = ordered
            .GroupBy(x => x.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatesByOrdinal.Count > 0)
            throw new NgbArgumentInvalidException(
                paramName: "dimensionRules",
                reason: $"Duplicate Ordinal in AccountDimensionRules: {string.Join(", ", duplicatesByOrdinal)}");

        return ordered;
    }
}
