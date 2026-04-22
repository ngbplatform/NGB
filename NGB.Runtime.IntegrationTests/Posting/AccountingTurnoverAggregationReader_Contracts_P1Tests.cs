using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingTurnoverAggregationReader_Contracts_P1Tests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";
    private const string Expense = "90.2";
    private const string Receivable = "62";

    [Fact]
    public async Task TurnoversTable_Equals_AggregationFromRegister_ForSameMonth()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedCoaAsync(host);

        var month = new DateOnly(2026, 1, 1);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        var p1 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var p2 = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var cp1 = Guid.CreateVersion7();
        var cp2 = Guid.CreateVersion7();

        // Act
        await PostDocAsync(host, doc1, p1, cashDebit: 100m, expenseCredit: 0m, arAmount: 30m, arCounterpartyId: cp1);
        await PostDocAsync(host, doc2, p2, cashDebit: 0m, expenseCredit: 40m, arAmount: 10m, arCounterpartyId: cp2);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var aggReader = sp.GetRequiredService<IAccountingTurnoverAggregationReader>();

        var fromTable = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);
        var fromRegister = await aggReader.GetAggregatedFromRegisterAsync(month, CancellationToken.None);

        var tableByKey = fromTable.ToDictionary(Key);
        var regByKey = fromRegister.ToDictionary(Key);

        tableByKey.Keys.Should().BeEquivalentTo(regByKey.Keys);

        foreach (var k in tableByKey.Keys)
        {
            var t = tableByKey[k];
            var r = regByKey[k];

            t.Period.Should().Be(r.Period);
            t.AccountId.Should().Be(r.AccountId);
            t.DimensionSetId.Should().Be(r.DimensionSetId);
            t.AccountCode.Should().Be(r.AccountCode);
            t.DebitAmount.Should().Be(r.DebitAmount);
            t.CreditAmount.Should().Be(r.CreditAmount);
        }
    }

    private static string Key(AccountingTurnover t)
    {
        return $"{t.Period:O}|{t.AccountId:N}|{t.DimensionSetId:N}";
    }

    private static async Task PostDocAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        decimal cashDebit,
        decimal expenseCredit,
        decimal arAmount,
        Guid arCounterpartyId)
    {
        await using var scopePosting = host.Services.CreateAsyncScope();
        var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                // Cash -> Revenue (debit cash)
                if (cashDebit > 0m)
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), cashDebit);

                // Expense -> Cash (credit cash)
                if (expenseCredit > 0m)
                    ctx.Post(documentId, periodUtc, chart.Get(Expense), chart.Get(Cash), expenseCredit);

                // AR -> Revenue (with dimensions)
                if (arAmount > 0m)
                    {
                        var ar = chart.Get(Receivable);
                        var rule = ar.DimensionRules.Single(r => string.Equals(r.DimensionCode, "counterparty", StringComparison.OrdinalIgnoreCase));
                        var debitBag = new DimensionBag(new[] { new DimensionValue(rule.DimensionId, arCounterpartyId) });
                        ctx.Post(documentId, periodUtc, ar, chart.Get(Revenue), arAmount, debitDimensions: debitBag);
                    }
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Expense,
            Name: "Expense",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Receivable,
            Name: "Accounts Receivable",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("counterparty", false, Ordinal: 10)
            ]
        ), CancellationToken.None);
    }
}
