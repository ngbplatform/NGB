using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_GetDraftSnapshot_Dimensions_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task GetDraft_ReturnsLinesWithDimensionBag_AndDimensionSetId()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, dimId) = await EnsureAccountsWithBuildingDimensionAsync(host);

        var buildingA = Guid.CreateVersion7();
        var buildingB = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        var docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            docId,
            [
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 50m,
                    Memo: "D",
                    Dimensions: [new DimensionValue(dimId, buildingA)]),

                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 50m,
                    Memo: "C",
                    Dimensions: [new DimensionValue(dimId, buildingB)])
            ],
            updatedBy: "u1",
            ct: CancellationToken.None);

        var snapshot = await gje.GetDraftAsync(docId, CancellationToken.None);

        snapshot.Document.Id.Should().Be(docId);
        snapshot.Document.Status.Should().Be(DocumentStatus.Draft);
        snapshot.Lines.Should().HaveCount(2);

        var debit = snapshot.Lines.Single(x => x.LineNo == 1);
        var credit = snapshot.Lines.Single(x => x.LineNo == 2);

        debit.DimensionSetId.Should().NotBe(Guid.Empty);
        credit.DimensionSetId.Should().NotBe(Guid.Empty);
        debit.DimensionSetId.Should().NotBe(credit.DimensionSetId);

        debit.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == dimId && x.ValueId == buildingA);
        credit.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == dimId && x.ValueId == buildingB);
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid dimId)> EnsureAccountsWithBuildingDimensionAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "building", IsRequired: true, Ordinal: 1)
                ]),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "building", IsRequired: true, Ordinal: 1)
                ]),
            CancellationToken.None);

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await coa.GetAsync(CancellationToken.None);

        var dimId = chart.Get(cashId).DimensionRules.Single().DimensionId;
        dimId.Should().NotBe(Guid.Empty);

        return (cashId, revenueId, dimId);
    }
}
