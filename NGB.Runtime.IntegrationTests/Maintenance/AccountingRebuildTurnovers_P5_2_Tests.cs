using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Maintenance;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Maintenance;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRebuildTurnovers_DimensionSetProjection_P5_2_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = new(2026, 1, 1);
    private static readonly DateTime Day15Utc = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RebuildTurnoversAsync_ReconstructsRows_PerAccountAndDimensionSet()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoaWithDimensionRulesAsync(host);

        var buildingA = Guid.CreateVersion7();
        var buildingB = Guid.CreateVersion7();

        // Snapshot expected turnover rows (by account + dimension set) before we corrupt data.
        object[] expectedSnapshot;


        // Two documents, same accounts, different "building" dimension values.
        // With real DimensionSet resolution enabled, turnovers must split by DimensionSetId (one per distinct dimension value).
        await PostAsync(host, Guid.CreateVersion7(), Day15Utc, debitCode: "50", creditCode: "90.1", amount: 10m, buildingA);
        await PostAsync(host, Guid.CreateVersion7(), Day15Utc.AddDays(1), debitCode: "50", creditCode: "90.1", amount: 20m,
            buildingB);

        // Sanity: turnovers exist and are split by DimensionSetId. Fixed-slot projection is removed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var t = await reader.GetForPeriodAsync(Period, CancellationToken.None);

            t.Should().HaveCount(4);

            // Cash: two debit buckets, one per dimension value.
            t.Should().ContainSingle(x =>
                x.AccountCode == "50"
                && x.Dimensions.Count == 1
                && x.Dimensions.Any(d => d.ValueId == buildingA)
                && x.DebitAmount == 10m
                && x.CreditAmount == 0m);

            t.Should().ContainSingle(x =>
                x.AccountCode == "50"
                && x.Dimensions.Count == 1
                && x.Dimensions.Any(d => d.ValueId == buildingB)
                && x.DebitAmount == 20m
                && x.CreditAmount == 0m);

            // Revenue: two credit buckets, one per dimension value.
            t.Should().ContainSingle(x =>
                x.AccountCode == "90.1"
                && x.Dimensions.Count == 1
                && x.Dimensions.Any(d => d.ValueId == buildingA)
                && x.DebitAmount == 0m
                && x.CreditAmount == 10m);

            t.Should().ContainSingle(x =>
                x.AccountCode == "90.1"
                && x.Dimensions.Count == 1
                && x.Dimensions.Any(d => d.ValueId == buildingB)
                && x.DebitAmount == 0m
                && x.CreditAmount == 20m);

            expectedSnapshot = t
                .Select(x => new
                {
                    x.AccountCode,
                    x.DimensionSetId,
                    ValueIds = x.Dimensions.Select(d => d.ValueId).ToArray(),
                    x.DebitAmount,
                    x.CreditAmount
                })
                .OrderBy(x => x.AccountCode)
                .ThenBy(x => x.DimensionSetId)
                .ToArray();
        }

        // Corrupt: wipe turnovers for the month.
        await DeleteTurnoversForPeriodAsync(Fixture.ConnectionString, Period);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reportReader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();
            (await reportReader.RunForPeriodAsync(Period, previousPeriodForChainCheck: null, CancellationToken.None))
                .TurnoversVsRegisterDiffCount.Should().BeGreaterThan(0);

            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            var written = await rebuild.RebuildTurnoversAsync(Period, CancellationToken.None);
            written.Should().BeGreaterThan(0);

            var afterReport = await reportReader.RunForPeriodAsync(Period, previousPeriodForChainCheck: null, CancellationToken.None);
            afterReport.IsOk.Should().BeTrue();
            afterReport.TurnoversVsRegisterDiffCount.Should().Be(0);
            afterReport.Issues.Should().BeEmpty();

            var reader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var after = await reader.GetForPeriodAsync(Period, CancellationToken.None);

            var afterSnapshot = after
                .Select(x => new
                {
                    x.AccountCode,
                    x.DimensionSetId,
                    ValueIds = x.Dimensions.Select(d => d.ValueId).ToArray(),
                    x.DebitAmount,
                    x.CreditAmount
                })
                .OrderBy(x => x.AccountCode)
                .ThenBy(x => x.DimensionSetId)
                .ToArray();
            afterSnapshot.Should().BeEquivalentTo(expectedSnapshot, options => options.WithStrictOrdering());
        }
    }

    private static async Task SeedCoaWithDimensionRulesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Both accounts have a single dimension rule ("building"). Fixed-slot projection is removed;
        // this rule exists only to generate DimensionSetId/DimensionBag.
        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("building", false, Ordinal: 10)
            ]
        ), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("building", false, Ordinal: 10)
            ]
        ), CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        Guid buildingValueId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get(debitCode);
                var credit = chart.Get(creditCode);

                var debitRule = debit.DimensionRules.Single(r =>
                    string.Equals(r.DimensionCode, "building", StringComparison.OrdinalIgnoreCase));
                var creditRule = credit.DimensionRules.Single(r =>
                    string.Equals(r.DimensionCode, "building", StringComparison.OrdinalIgnoreCase));

                var debitBag = new DimensionBag([new DimensionValue(debitRule.DimensionId, buildingValueId)]);
                var creditBag = new DimensionBag([new DimensionValue(creditRule.DimensionId, buildingValueId)]);

                ctx.Post(
                    documentId: documentId,
                    period: periodUtc,
                    debit: debit,
                    credit: credit,
                    amount: amount,
                    debitDimensions: debitBag,
                    creditDimensions: creditBag);
            },
            CancellationToken.None);
    }

    private static async Task DeleteTurnoversForPeriodAsync(string cs, DateOnly period)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        const string sql = "DELETE FROM accounting_turnovers WHERE period = @period;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("period", period);
        await cmd.ExecuteNonQueryAsync();
    }
}
