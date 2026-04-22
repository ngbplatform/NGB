using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class Turnovers_EmptyAndNonEmptyDimensions_Stress_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_ManyRows_WithEmptyAndNonEmptyDimensions_GroupsTurnoversCorrectly()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedCoaAsync(host);

        var period = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(period);

        var customerA = Guid.CreateVersion7();
        var customerB = Guid.CreateVersion7();
        var project1 = Guid.CreateVersion7();
        var project2 = Guid.CreateVersion7();

        var documentId = Guid.CreateVersion7();
        const int repetitions = 200;

        // Act
        await using (var scopePosting = host.Services.CreateAsyncScope())
        {
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get(Cash);
                    var revenue = chart.Get(Revenue);

                    var customerDimensionId =
                        revenue.DimensionRules.Single(x => x.DimensionCode == "customer").DimensionId;
                    var projectDimensionId =
                        revenue.DimensionRules.Single(x => x.DimensionCode == "project").DimensionId;

                    var creditCustomerA = new DimensionBag(new[]
                    {
                        new DimensionValue(customerDimensionId, customerA)
                    });

                    var creditCustomerAProject1 = new DimensionBag(new[]
                    {
                        new DimensionValue(customerDimensionId, customerA),
                        new DimensionValue(projectDimensionId, project1)
                    });

                    var creditProject2Only = new DimensionBag(new[]
                    {
                        new DimensionValue(projectDimensionId, project2)
                    });

                    var creditCustomerBProject2 = new DimensionBag(new[]
                    {
                        new DimensionValue(customerDimensionId, customerB),
                        new DimensionValue(projectDimensionId, project2)
                    });

                    for (var i = 0; i < repetitions; i++)
                    {
                        // creditDimensions: null => empty bag
                        ctx.Post(documentId, period, cash, revenue, 10m, creditDimensions: null);

                        // customer only
                        ctx.Post(documentId, period, cash, revenue, 20m, creditDimensions: creditCustomerA);

                        // customer + project1
                        ctx.Post(documentId, period, cash, revenue, 30m, creditDimensions: creditCustomerAProject1);

                        // project2 only (customer missing and not required)
                        ctx.Post(documentId, period, cash, revenue, 40m, creditDimensions: creditProject2Only);

                        // customerB + project2
                        ctx.Post(documentId, period, cash, revenue, 50m, creditDimensions: creditCustomerBProject2);
                    }
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var turnovers = await scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>()
            .GetForPeriodAsync(month, CancellationToken.None);

        // Cash: no dimensions => single bucket
        var totalPerIteration = 10m + 20m + 30m + 40m + 50m;
        var expectedTotal = totalPerIteration * repetitions;

        // We expect:
        //  - Cash: a single bucket (no dimensions, Guid.Empty).
        //  - Revenue: 5 distinct buckets by DimensionSetId, reflecting the 5 different dimension combinations posted below.
        turnovers.Should().HaveCount(6);

        var cash = turnovers.Should().ContainSingle(x => x.AccountCode == Cash && x.DimensionSetId == Guid.Empty).Which;
        cash.Dimensions.Should().BeEmpty();
        cash.DebitAmount.Should().Be(expectedTotal);
        cash.CreditAmount.Should().Be(0m);

        var revenueBuckets = turnovers.Where(x => x.AccountCode == Revenue).ToList();
        revenueBuckets.Should().HaveCount(5);

        revenueBuckets.Should().ContainSingle(x =>
            x.DimensionSetId == Guid.Empty
            && x.Dimensions.Count == 0
            && x.DebitAmount == 0m
            && x.CreditAmount == 10m * repetitions);

        revenueBuckets.Should().ContainSingle(x =>
            x.DimensionSetId != Guid.Empty
            && x.Dimensions.Count == 1
            && x.Dimensions.Any(d => d.ValueId == customerA)
            && x.DebitAmount == 0m
            && x.CreditAmount == 20m * repetitions);

        revenueBuckets.Should().ContainSingle(x =>
            x.DimensionSetId != Guid.Empty
            && x.Dimensions.Count == 2
            && x.Dimensions.Any(d => d.ValueId == customerA)
            && x.Dimensions.Any(d => d.ValueId == project1)
            && x.DebitAmount == 0m
            && x.CreditAmount == 30m * repetitions);

        revenueBuckets.Should().ContainSingle(x =>
            x.DimensionSetId != Guid.Empty
            && x.Dimensions.Count == 1
            && x.Dimensions.Any(d => d.ValueId == project2)
            && x.DebitAmount == 0m
            && x.CreditAmount == 40m * repetitions);

        revenueBuckets.Should().ContainSingle(x =>
            x.DimensionSetId != Guid.Empty
            && x.Dimensions.Count == 2
            && x.Dimensions.Any(d => d.ValueId == customerB)
            && x.Dimensions.Any(d => d.ValueId == project2)
            && x.DebitAmount == 0m
            && x.CreditAmount == 50m * repetitions);
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("customer", false, Ordinal: 10),
                new AccountDimensionRuleRequest("project", false, Ordinal: 20),
            },
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
