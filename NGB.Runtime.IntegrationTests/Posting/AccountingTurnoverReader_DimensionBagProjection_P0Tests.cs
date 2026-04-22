using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingTurnoverReader_DimensionBagProjection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetForPeriodAsync_Populates_Dimensions_FromDimensionSetItems()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Debit account: requires a single dimension.
        await coa.CreateAsync(new CreateAccountRequest(
            Code: "1010",
            Name: "Cash",
            Type: AccountType.Asset,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest(DimensionCode: "counterparty", IsRequired: true, Ordinal: 1)
            }));

        // Credit account: no dimensions.
        await coa.CreateAsync(new CreateAccountRequest(
            Code: "9010",
            Name: "Revenue",
            Type: AccountType.Income));

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync();
        var debit = chart.Get("1010");
        var credit = chart.Get("9010");

        var dimId = debit.DimensionRules.Single().DimensionId;
        var valueId = Guid.CreateVersion7();

        var debitBag = new DimensionBag(new[] { new DimensionValue(dimId, valueId) });

        var engine = sp.GetRequiredService<PostingEngine>();
        var docId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        await engine.PostAsync(PostingOperation.Post, (ctx, ct) =>
        {
            ctx.Post(
                documentId: docId,
                period: periodUtc,
                debit: debit,
                credit: credit,
                amount: 100m,
                debitDimensions: debitBag,
                creditDimensions: DimensionBag.Empty);

            return Task.CompletedTask;
        }, manageTransaction: true, CancellationToken.None);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var rows = await turnoverReader.GetForPeriodAsync(new DateOnly(2026, 1, 1), CancellationToken.None);

        rows.Should().ContainSingle(x => x.AccountId == debit.Id && x.DimensionSetId != Guid.Empty);

        var row = rows.Single(x => x.AccountId == debit.Id && x.DimensionSetId != Guid.Empty);

        row.Dimensions.Should().Contain(new DimensionValue(dimId, valueId));
        row.Dimensions.Should().ContainSingle(x => x.ValueId == valueId);
    }
}