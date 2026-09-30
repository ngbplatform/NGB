using FluentAssertions;
using NGB.Accounting.Balances;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using Xunit;

namespace NGB.Accounting.Tests.Balances;

public sealed class AccountingBalanceCalculatorTests
{
    [Fact]
    public void Calculate_CoversPreviousTurnoverAndEmptyFallbacks()
    {
        var previousOnlyAccountId = Guid.CreateVersion7();
        var turnoverOnlyAccountId = Guid.CreateVersion7();
        var combinedAccountId = Guid.CreateVersion7();
        var previousDimensions = CreateDimensions();
        var turnoverDimensions = CreateDimensions();

        AccountingBalance[] previous =
        [
            new()
            {
                AccountId = previousOnlyAccountId,
                DimensionSetId = Guid.Empty,
                Dimensions = null!,
                ClosingBalance = 25m
            },
            new()
            {
                AccountId = combinedAccountId,
                DimensionSetId = Guid.Empty,
                Dimensions = previousDimensions,
                AccountCode = null,
                ClosingBalance = 100m
            }
        ];
        AccountingTurnover[] turnovers =
        [
            new()
            {
                AccountId = turnoverOnlyAccountId,
                DimensionSetId = Guid.Empty,
                Dimensions = null!,
                DebitAmount = 40m,
                CreditAmount = 10m
            },
            new()
            {
                AccountId = combinedAccountId,
                DimensionSetId = Guid.Empty,
                Dimensions = turnoverDimensions,
                AccountCode = "1010",
                DebitAmount = 5m,
                CreditAmount = 20m
            }
        ];

        var result = new AccountingBalanceCalculator()
            .Calculate(turnovers, previous, new DateOnly(2026, 5, 19))
            .ToDictionary(x => x.AccountId);

        result[previousOnlyAccountId].Dimensions.Should().BeSameAs(DimensionBag.Empty);
        result[previousOnlyAccountId].OpeningBalance.Should().Be(25m);
        result[previousOnlyAccountId].ClosingBalance.Should().Be(25m);

        result[turnoverOnlyAccountId].Dimensions.Should().BeSameAs(DimensionBag.Empty);
        result[turnoverOnlyAccountId].OpeningBalance.Should().Be(0m);
        result[turnoverOnlyAccountId].ClosingBalance.Should().Be(30m);

        result[combinedAccountId].Dimensions.Should().BeSameAs(previousDimensions);
        result[combinedAccountId].AccountCode.Should().Be("1010");
        result[combinedAccountId].OpeningBalance.Should().Be(100m);
        result[combinedAccountId].ClosingBalance.Should().Be(85m);
        result.Values.Should().OnlyContain(x => x.Period == new DateOnly(2026, 5, 1));
    }

    private static DimensionBag CreateDimensions()
        => new([new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())]);
}
