using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P0: Unpost/Repost storno must preserve full DimensionSet (including dimensions beyond the first three values).
/// Otherwise, analytics by dimensions becomes inconsistent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnpostRepost_Storno_PreservesFullDimensionSets_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAsync_WhenOriginalEntryHasFourDimensions_StornoPreservesAllDimensions()
    {
        using var host = CreateHost();
        await SeedCoaWithFourDimensionsAsync(host);

        var docId = Guid.CreateVersion7();
        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var (debitBag, creditBag) = await BuildBagsAsync(host);
        await PostAsync(host, docId, dateUtc, amount: 100m, debitBag, creditBag);

        var original = (await ReadEntriesAsync(host, docId)).Single(x => !x.IsStorno);
        original.DebitDimensions.Count.Should().Be(4);
        original.CreditDimensions.Count.Should().Be(4);
        original.DebitDimensionSetId.Should().NotBe(Guid.Empty);
        original.CreditDimensionSetId.Should().NotBe(Guid.Empty);

        await UnpostAsync(host, docId);

        var after = await ReadEntriesAsync(host, docId);
        after.Should().HaveCount(2);

        var storno = after.Single(x => x.IsStorno);

        storno.DebitDimensionSetId.Should().Be(original.CreditDimensionSetId);
        storno.CreditDimensionSetId.Should().Be(original.DebitDimensionSetId);

        storno.DebitDimensions.Items.Should().BeEquivalentTo(original.CreditDimensions.Items);
        storno.CreditDimensions.Items.Should().BeEquivalentTo(original.DebitDimensions.Items);

        storno.DebitDimensions.Count.Should().Be(4);
        storno.CreditDimensions.Count.Should().Be(4);
    }

    [Fact]
    public async Task RepostAsync_WhenOriginalEntryHasFourDimensions_StornoPreservesAllDimensions()
    {
        using var host = CreateHost();
        await SeedCoaWithFourDimensionsAsync(host);

        var docId = Guid.CreateVersion7();
        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var (debitBag, creditBag) = await BuildBagsAsync(host);
        await PostAsync(host, docId, dateUtc, amount: 100m, debitBag, creditBag);

        var before = await ReadEntriesAsync(host, docId);
        var original = before.Single(x => !x.IsStorno);

        await RepostAsync(host, docId, dateUtc, amount: 200m, debitBag, creditBag);

        var after = await ReadEntriesAsync(host, docId);
        after.Should().HaveCount(3);

        var storno = after.Single(x => x.IsStorno);

        storno.DebitDimensionSetId.Should().Be(original.CreditDimensionSetId);
        storno.CreditDimensionSetId.Should().Be(original.DebitDimensionSetId);

        storno.DebitDimensions.Count.Should().Be(4);
        storno.CreditDimensions.Count.Should().Be(4);

        storno.DebitDimensions.Items.Should().BeEquivalentTo(original.CreditDimensions.Items);
        storno.CreditDimensions.Items.Should().BeEquivalentTo(original.DebitDimensions.Items);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task SeedCoaWithFourDimensionsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Two accounts with the same 4 dimensions, using non-1..3 ordinals to ensure "sort by Ordinal" is honored.
        var rules = new[]
        {
            new AccountDimensionRuleRequest("d1", IsRequired: true, Ordinal: 10),
            new AccountDimensionRuleRequest("d2", IsRequired: true, Ordinal: 20),
            new AccountDimensionRuleRequest("d3", IsRequired: true, Ordinal: 30),
            new AccountDimensionRuleRequest("d4", IsRequired: false, Ordinal: 40),
        };

        await coa.CreateAsync(new CreateAccountRequest(
            Code: "A100",
            Name: "Asset with 4 dims",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            DimensionRules: rules),
            CancellationToken.None);

        await coa.CreateAsync(new CreateAccountRequest(
            Code: "I200",
            Name: "Income with 4 dims",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            DimensionRules: rules),
            CancellationToken.None);
    }

    private static async Task<(DimensionBag debit, DimensionBag credit)> BuildBagsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var chartProvider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await chartProvider.GetAsync(CancellationToken.None);

        var debit = chart.Get("A100");
        var credit = chart.Get("I200");

        // Both accounts share the same rules, so take dimension ids from either.
        debit.DimensionRules.Should().HaveCount(4);
        credit.DimensionRules.Should().HaveCount(4);

        var d1 = debit.DimensionRules[0].DimensionId;
        var d2 = debit.DimensionRules[1].DimensionId;
        var d3 = debit.DimensionRules[2].DimensionId;
        var d4 = debit.DimensionRules[3].DimensionId;

        // Deterministic value ids to make assertions stable.
        var v1 = new Guid("11111111-1111-1111-1111-111111111111");
        var v2 = new Guid("22222222-2222-2222-2222-222222222222");
        var v3 = new Guid("33333333-3333-3333-3333-333333333333");
        var va = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var vb = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var debitBag = new DimensionBag([
            new DimensionValue(d1, v1),
            new DimensionValue(d2, v2),
            new DimensionValue(d3, v3),
            new DimensionValue(d4, va)
        ]);

        var creditBag = new DimensionBag([
            new DimensionValue(d1, v1),
            new DimensionValue(d2, v2),
            new DimensionValue(d3, v3),
            new DimensionValue(d4, vb)
        ]);

        return (debitBag, creditBag);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        decimal amount,
        DimensionBag debitDimensions,
        DimensionBag creditDimensions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("A100"), chart.Get("I200"), amount, debitDimensions, creditDimensions);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await svc.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        decimal amount,
        DimensionBag debitDimensions,
        DimensionBag creditDimensions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<RepostingService>();

        await svc.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("A100"), chart.Get("I200"), amount, debitDimensions, creditDimensions);
        }, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<NGB.Accounting.Registers.AccountingEntry>> ReadEntriesAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        return await reader.GetByDocumentAsync(documentId, CancellationToken.None);
    }
}
