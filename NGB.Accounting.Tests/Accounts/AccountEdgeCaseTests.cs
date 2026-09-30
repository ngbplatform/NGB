using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Accounts;

public sealed class AccountEdgeCaseTests
{
    [Fact]
    public void Normalize_WhitespaceCode_Throws()
    {
        var act = () => AccountCode.Normalize(" \t ");

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Constructor_DuplicateDimensionId_Throws()
    {
        var dimensionId = Guid.CreateVersion7();
        AccountDimensionRule[] rules =
        [
            new(dimensionId, "warehouse", 10, true),
            new(dimensionId, "warehouse-copy", 20, false)
        ];

        var act = () => CreateAccount(rules);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Duplicate DimensionId*");
    }

    [Fact]
    public void Constructor_DuplicateDimensionOrdinal_Throws()
    {
        AccountDimensionRule[] rules =
        [
            new(Guid.CreateVersion7(), "warehouse", 10, true),
            new(Guid.CreateVersion7(), "project", 10, false)
        ];

        var act = () => CreateAccount(rules);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Duplicate Ordinal*");
    }

    [Fact]
    public void ApplyContra_UnknownNormalBalance_Throws()
    {
        var act = () => ((NormalBalance)int.MaxValue).ApplyContra(isContra: true);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(NormalBalance.Debit, false, NormalBalance.Debit)]
    [InlineData(NormalBalance.Credit, false, NormalBalance.Credit)]
    [InlineData(NormalBalance.Debit, true, NormalBalance.Credit)]
    [InlineData(NormalBalance.Credit, true, NormalBalance.Debit)]
    public void ApplyContra_AllValidCombinations_ReturnExpected(
        NormalBalance value,
        bool isContra,
        NormalBalance expected)
    {
        value.ApplyContra(isContra).Should().Be(expected);
    }

    [Fact]
    public void FromStatementSection_UnknownSection_Throws()
    {
        var act = () => NormalBalanceDefaults.FromStatementSection((StatementSection)short.MaxValue);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(StatementSection.Assets, NormalBalance.Debit)]
    [InlineData(StatementSection.Liabilities, NormalBalance.Credit)]
    [InlineData(StatementSection.Equity, NormalBalance.Credit)]
    [InlineData(StatementSection.Income, NormalBalance.Credit)]
    [InlineData(StatementSection.CostOfGoodsSold, NormalBalance.Debit)]
    [InlineData(StatementSection.Expenses, NormalBalance.Debit)]
    [InlineData(StatementSection.OtherIncome, NormalBalance.Credit)]
    [InlineData(StatementSection.OtherExpense, NormalBalance.Debit)]
    public void FromStatementSection_AllKnownSections_ReturnExpected(
        StatementSection section,
        NormalBalance expected)
    {
        NormalBalanceDefaults.FromStatementSection(section).Should().Be(expected);
    }

    [Fact]
    public void FromAccountType_UnknownType_Throws()
    {
        var act = () => StatementSectionDefaults.FromAccountType((AccountType)int.MaxValue);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(AccountType.Asset, StatementSection.Assets)]
    [InlineData(AccountType.Liability, StatementSection.Liabilities)]
    [InlineData(AccountType.Equity, StatementSection.Equity)]
    [InlineData(AccountType.Income, StatementSection.Income)]
    [InlineData(AccountType.Expense, StatementSection.Expenses)]
    public void FromAccountType_AllKnownTypes_ReturnExpected(AccountType type, StatementSection expected)
    {
        StatementSectionDefaults.FromAccountType(type).Should().Be(expected);
    }

    private static Account CreateAccount(IReadOnlyList<AccountDimensionRule> rules)
        => new(
            id: Guid.CreateVersion7(),
            code: "1010",
            name: "Cash",
            type: AccountType.Asset,
            dimensionRules: rules);
}
