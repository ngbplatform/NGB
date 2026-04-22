using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// C8-08b: CloseFiscalYear must operate on (account + DimensionSetId), not on a truncated projection of the first 3 dimensions.
///
/// We create two different dimension sets that share the same first three dimension values,
/// but differ in the 4th dimension. If FY close or Trial Balance aggregation uses only
/// a truncated fixed-slot projection, those two sets would be collapsed into one row/one closing entry.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_DimensionSets_NotCollapsed_ByDimensionSetId_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_WhenTwoDimensionSetsSharePrefixValues_ShouldCloseSeparately_PerDimensionSet()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaAsync(host);

        // Dimension IDs are deterministic per normalized code.
        var dim1 = DeterministicGuid.Create("Dimension|buildings");
        var dim2 = DeterministicGuid.Create("Dimension|counterparties");
        var dim3 = DeterministicGuid.Create("Dimension|contracts");
        var dim4 = DeterministicGuid.Create("Dimension|floors");

        // Two different dimension sets:
        // - Same first 3 values (=> same a truncated projection of the first 3 dimensions)
        // - Different 4th value (=> different DimensionSetId)
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();
        var v4a = Guid.CreateVersion7();
        var v4b = Guid.CreateVersion7();

        var bagA = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2),
            new DimensionValue(dim3, v3),
            new DimensionValue(dim4, v4a)
        });

        var bagB = new DimensionBag(new[]
        {
            new DimensionValue(dim1, v1),
            new DimensionValue(dim2, v2),
            new DimensionValue(dim3, v3),
            new DimensionValue(dim4, v4b)
        });

        // Create P&L activity for both sets.
        // Revenue account is credit-normal.
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m, revenueDimensions: bagA);
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 200m, revenueDimensions: bagB);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var dimReader = sp.GetRequiredService<IDimensionSetReader>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();

            // 1) Closing entries: TWO separate entries for the revenue account (one per dimension set), not collapsed.
            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);

            var revenueEntries = entries
                .Where(e => e.Debit.Code == "90.1" && e.Credit.Code == "300")
                .ToList();

            revenueEntries.Should().HaveCount(2);
            revenueEntries.Select(e => e.Amount).OrderBy(x => x).Should().Equal(100m, 200m);

            var setIds = revenueEntries.Select(e => e.DebitDimensionSetId).Distinct().ToList();
            setIds.Should().HaveCount(2);
            setIds.Should().NotContain(Guid.Empty);

            var bagsById = await dimReader.GetBagsByIdsAsync(setIds, CancellationToken.None);

            // Prove that the two sets differ (the 4th dimension), while having identical first three values.
            bagsById.Values.Select(b => b.Items.Single(x => x.DimensionId == dim4).ValueId)
                .Should().BeEquivalentTo(new[] { v4a, v4b });

            bagsById.Values.All(b =>
                    b.Items.Single(x => x.DimensionId == dim1).ValueId == v1 &&
                    b.Items.Single(x => x.DimensionId == dim2).ValueId == v2 &&
                    b.Items.Single(x => x.DimensionId == dim3).ValueId == v3)
                .Should().BeTrue();

            // Debit side of closing entries is the P&L account -> dimensions must be preserved per set.
            revenueEntries.Select(e => e.DebitDimensionSetId).Should().BeEquivalentTo(setIds);
            revenueEntries.All(e => e.CreditDimensionSetId == Guid.Empty).Should().BeTrue("retained earnings should not carry dimensions");

            // 2) Trial balance should keep two separate rows for the revenue account (account + DimensionSetId).
            var tb = await trialBalance.GetAsync(endPeriod, endPeriod, CancellationToken.None);
            var revenueRows = tb.Where(r => r.AccountCode == "90.1").ToList();
            revenueRows.Should().HaveCount(2);
            revenueRows.Select(r => r.DimensionSetId).Should().BeEquivalentTo(setIds);
            revenueRows.All(r => r.ClosingBalance == 0m).Should().BeTrue("FY close must zero-out each P&L slice per dimension set");

            // Dimensions preserved (the 4th differs).
            revenueRows.All(r => r.Dimensions.Any(d => d.ValueId == v1)
                                 && r.Dimensions.Any(d => d.ValueId == v2)
                                 && r.Dimensions.Any(d => d.ValueId == v3))
                .Should().BeTrue("DimensionBag must contain all 3 dimension values");
            revenueRows.Select(r => r.Dimensions.Items.Single(x => x.DimensionId == dim4).ValueId)
                .Should().BeEquivalentTo(new[] { v4a, v4b });
        }
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet account
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Retained Earnings (Equity, credit-normal)
        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Revenue (P&L) with 4 dimension rules, ordinals intentionally not 1..3.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20),
                new AccountDimensionRuleRequest("Contracts", IsRequired: false, Ordinal: 30),
                new AccountDimensionRuleRequest("Floors", IsRequired: false, Ordinal: 40)
            }
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }

    private static async Task PostRevenueAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount, DimensionBag revenueDimensions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(
                    documentId,
                    periodUtc,
                    debit,
                    credit,
                    amount,
                    debitDimensions: DimensionBag.Empty,
                    creditDimensions: revenueDimensions);
            },
            ct: CancellationToken.None);
    }
}
