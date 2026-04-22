using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using Xunit;

namespace NGB.Accounting.Tests.Turnovers;

public sealed class AccountingTurnoverCalculator_DimensionBagProjection_P0Tests
{
    [Fact]
    public void Calculate_WhenDimensionsProvided_PreservesDimensionBag()
    {
        var r1 = new AccountDimensionRule(Guid.CreateVersion7(), "d1", isRequired: true, ordinal: 10);
        var r2 = new AccountDimensionRule(Guid.CreateVersion7(), "d2", isRequired: false, ordinal: 20);
        var r3 = new AccountDimensionRule(Guid.CreateVersion7(), "d3", isRequired: false, ordinal: 30);
        var r4 = new AccountDimensionRule(Guid.CreateVersion7(), "d4", isRequired: false, ordinal: 40);

        var debit = new Account(
            id: Guid.CreateVersion7(),
            code: "1100",
            name: "Cash",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false,
            dimensionRules: [r1, r2, r3, r4]);

        var credit = new Account(
            id: Guid.CreateVersion7(),
            code: "4100",
            name: "Revenue",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            negativeBalancePolicy: NegativeBalancePolicy.Allow);

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();
        var v4 = Guid.CreateVersion7();

        var bag = new DimensionBag([
            new DimensionValue(r1.DimensionId, v1),
            new DimensionValue(r2.DimensionId, v2),
            new DimensionValue(r3.DimensionId, v3),
            new DimensionValue(r4.DimensionId, v4)
        ]);

        var entry = new AccountingEntry
        {
            DocumentId = Guid.CreateVersion7(),
            Period = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Debit = debit,
            Credit = credit,
            Amount = 100m,
            DebitDimensions = bag,
            DebitDimensionSetId = Guid.CreateVersion7(),
            CreditDimensions = DimensionBag.Empty,
            CreditDimensionSetId = Guid.Empty
        };

        var calc = new AccountingTurnoverCalculator();
        var turnovers = calc.Calculate([entry]);
        var turnover = turnovers.Single(t => t.AccountId == debit.Id);

        turnover.Dimensions.Items.Should().Equal(bag.Items);
    }
}
