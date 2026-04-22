using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Core.Dimensions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_Dimensions_Canonicalization_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task ReplaceDraftLines_WhenDimensionsProvidedInDifferentOrder_ProducesSameDimensionSetId_AndCanonicalBag()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, orderedRules) = await EnsureAccountsWithFourDimensionsAsync(host);

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();
        var v4 = Guid.CreateVersion7();

        // Same set of dimensions, different order.
        var dimsOrderA = new[]
        {
            new DimensionValue(orderedRules[0].DimensionId, v1),
            new DimensionValue(orderedRules[1].DimensionId, v2),
            new DimensionValue(orderedRules[2].DimensionId, v3),
            new DimensionValue(orderedRules[3].DimensionId, v4),
        };

        var dimsOrderB = new[]
        {
            new DimensionValue(orderedRules[3].DimensionId, v4),
            new DimensionValue(orderedRules[1].DimensionId, v2),
            new DimensionValue(orderedRules[0].DimensionId, v1),
            new DimensionValue(orderedRules[2].DimensionId, v3),
        };

        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var docDateUtc = new DateTime(2026, 01, 11, 12, 0, 0, DateTimeKind.Utc);
        var docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            docId,
            [
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 100m,
                    Memo: "D",
                    Dimensions: dimsOrderA),

                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 100m,
                    Memo: "C",
                    Dimensions: dimsOrderB)
            ],
            updatedBy: "u1",
            ct: CancellationToken.None);

        var snapshot = await gje.GetDraftAsync(docId, CancellationToken.None);
        snapshot.Lines.Should().HaveCount(2);

        var debit = snapshot.Lines.Single(x => x.LineNo == 1);
        var credit = snapshot.Lines.Single(x => x.LineNo == 2);

        debit.DimensionSetId.Should().NotBe(Guid.Empty);
        credit.DimensionSetId.Should().NotBe(Guid.Empty);

        // DimensionSetId is derived from a canonicalized DimensionBag, so ordering must not matter.
        debit.DimensionSetId.Should().Be(credit.DimensionSetId);

        debit.Dimensions.Items.Select(x => x.DimensionId).Should().BeInAscendingOrder();
        debit.Dimensions.Items.Should().HaveCount(4);

        debit.Dimensions.Items.Should().Contain(x => x.DimensionId == orderedRules[0].DimensionId && x.ValueId == v1);
        debit.Dimensions.Items.Should().Contain(x => x.DimensionId == orderedRules[1].DimensionId && x.ValueId == v2);
        debit.Dimensions.Items.Should().Contain(x => x.DimensionId == orderedRules[2].DimensionId && x.ValueId == v3);
        debit.Dimensions.Items.Should().Contain(x => x.DimensionId == orderedRules[3].DimensionId && x.ValueId == v4);
    }

    private static async Task<(Guid cashId, Guid revenueId, AccountDimensionRule[] orderedRules)> EnsureAccountsWithFourDimensionsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var rules = new[]
        {
            new AccountDimensionRuleRequest(DimensionCode: "d1", IsRequired: true, Ordinal: 10),
            new AccountDimensionRuleRequest(DimensionCode: "d2", IsRequired: false, Ordinal: 20),
            new AccountDimensionRuleRequest(DimensionCode: "d3", IsRequired: false, Ordinal: 30),
            new AccountDimensionRuleRequest(DimensionCode: "d4", IsRequired: false, Ordinal: 40),
        };

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1100",
                Name: "Cash (4 dims)",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: rules),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4100",
                Name: "Revenue (4 dims)",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: rules),
            CancellationToken.None);

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await coa.GetAsync(CancellationToken.None);

        var ordered = chart.Get(cashId).DimensionRules
            .OrderBy(r => r.Ordinal)
            .ToArray();

        ordered.Should().HaveCount(4);
        ordered[0].Ordinal.Should().Be(10);
        ordered[3].Ordinal.Should().Be(40);

        return (cashId, revenueId, ordered);
    }
}
