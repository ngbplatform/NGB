using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Core.Dimensions;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class AccountingPostingContext_DimensionsTests
{
    [Fact]
    public void Post_PreservesDimensions_OnBothSides()
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
            dimensionRules: new[] { r1, r2, r3, r4 });

        var credit = new Account(
            id: Guid.CreateVersion7(),
            code: "4100",
            name: "Revenue",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false,
            dimensionRules: new[] { r1, r2, r3, r4 });

        var coa = new ChartOfAccounts();
        coa.Add(debit);
        coa.Add(credit);

        var ctx = new AccountingPostingContext(coa);

        var dv1 = Guid.CreateVersion7();
        var dv2 = Guid.CreateVersion7();
        var dv3 = Guid.CreateVersion7();
        var dv4 = Guid.CreateVersion7();

        var debitBag = new DimensionBag(new[]
        {
            new DimensionValue(r1.DimensionId, dv1),
            new DimensionValue(r2.DimensionId, dv2),
            new DimensionValue(r3.DimensionId, dv3),
            new DimensionValue(r4.DimensionId, dv4)
        });

        ctx.Post(
            documentId: Guid.CreateVersion7(),
            period: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit: debit,
            credit: credit,
            amount: 100m,
            debitDimensions: debitBag,
            creditDimensions: DimensionBag.Empty);

        ctx.Entries.Should().HaveCount(1);
        var e = ctx.Entries[0];

        e.DebitDimensions.Items.Should().Contain(new DimensionValue(r1.DimensionId, dv1));
        e.DebitDimensions.Items.Should().Contain(new DimensionValue(r2.DimensionId, dv2));
        e.DebitDimensions.Items.Should().Contain(new DimensionValue(r3.DimensionId, dv3));
        e.DebitDimensions.Items.Should().Contain(new DimensionValue(r4.DimensionId, dv4));

        e.CreditDimensions.IsEmpty.Should().BeTrue();
    }
}
