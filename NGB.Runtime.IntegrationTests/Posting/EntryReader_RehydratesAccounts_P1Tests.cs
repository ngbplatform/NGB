using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public sealed class EntryReader_RehydratesAccountsAndDimensionRules_P1Tests(PostgresTestFixture fixture)
{
    private const string Receivable = "62";
    private const string Revenue = "90.1";

    [Fact]
    public async Task GetByDocumentAsync_ReturnsAccountsWithDimensionRules_AndNegativeBalancePolicy()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedCoaAsync(host);

        var period = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var counterparty = Guid.CreateVersion7();
        var building = Guid.CreateVersion7();

        // Act
        await using (var scopePosting = host.Services.CreateAsyncScope())
        {
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var ar = chart.Get(Receivable);
                    var cpRule = ar.DimensionRules.Single(r => string.Equals(r.DimensionCode, "counterparty", StringComparison.OrdinalIgnoreCase));
                    var bldRule = ar.DimensionRules.Single(r => string.Equals(r.DimensionCode, "building", StringComparison.OrdinalIgnoreCase));

                    var debitBag = new DimensionBag(new[]
                    {
                        new DimensionValue(cpRule.DimensionId, counterparty),
                        new DimensionValue(bldRule.DimensionId, building)
                    });

                    ctx.Post(
                        documentId,
                        period,
                        ar,
                        chart.Get(Revenue),
                        123.45m,
                        debitDimensions: debitBag);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().HaveCount(1);

        var e = entries[0];
        e.DocumentId.Should().Be(documentId);
        e.Period.Should().Be(period);
        e.Amount.Should().Be(123.45m);

        e.Debit.Code.Should().Be(Receivable);

        e.Debit.DimensionRules.Should().HaveCount(2);

        var rules = e.Debit.DimensionRules
            .OrderBy(r => r.Ordinal)
            .ToArray();

        rules[0].DimensionCode.Should().Be("counterparty");
        rules[0].IsRequired.Should().BeTrue();

        rules[1].DimensionCode.Should().Be("building");
        rules[1].IsRequired.Should().BeFalse();

        e.Debit.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Warn);

        e.Credit.Code.Should().Be(Revenue);
        e.Credit.DimensionRules.Should().BeEmpty();
        e.Credit.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Allow);

        e.DebitDimensions.Items.Should().HaveCount(2);
        e.DebitDimensions.Items.Should().Contain(new DimensionValue(rules[0].DimensionId, counterparty));
        e.DebitDimensions.Items.Should().Contain(new DimensionValue(rules[1].DimensionId, building));
        e.CreditDimensions.IsEmpty.Should().BeTrue();
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Receivable,
            Name: "Accounts Receivable",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("counterparty", true, Ordinal: 10),
                new AccountDimensionRuleRequest("building", false, Ordinal: 20)
            ],
            NegativeBalancePolicy: NegativeBalancePolicy.Warn
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
