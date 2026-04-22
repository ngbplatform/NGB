using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountCard_DimensionValueEnrichment_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CashDimensionCode = "it_cat_dim_cash";
    private const string RevenueDimensionCode = "it_cat_dim_rev";

    private const string CashTableName = "cat_it_cat_dim_cash";
    private const string RevenueTableName = "cat_it_cat_dim_rev";

    private const string DisplayColumn = "name";

    [Fact]
    public async Task AccountCard_AttachesDimensionValueDisplays_ForSelectedAndCounterSide()
    {
        await EnsureTypedTablesExistAsync(Fixture.ConnectionString);

        var cashValueId = Guid.CreateVersion7();
        var revenueValueId = Guid.CreateVersion7();

        await SeedAsync(Fixture.ConnectionString, CashTableName, cashValueId, "Cash A");
        await SeedAsync(Fixture.ConnectionString, RevenueTableName, revenueValueId, "Revenue B");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId) = await SeedCoAAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();

            registry.Register(new CatalogTypeMetadata(
                CatalogCode: CashDimensionCode,
                DisplayName: "IT Cash Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata(CashTableName, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "it")));

            registry.Register(new CatalogTypeMetadata(
                CatalogCode: RevenueDimensionCode,
                DisplayName: "IT Revenue Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata(RevenueTableName, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "it")));
        }

        var documentId = Guid.CreateVersion7();
        await PostWithDimensionsAsync(
            host,
            documentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            cashValueId,
            revenueValueId);

        var cashDimensionId = DeterministicGuid.Create($"Dimension|{CashDimensionCode.Trim().ToLowerInvariant()}");
        var revenueDimensionId = DeterministicGuid.Create($"Dimension|{RevenueDimensionCode.Trim().ToLowerInvariant()}");

        // Low-level page reader: ensures BOTH selected-side and counter-side enrichment is present.
        await using (var scope2 = host.Services.CreateAsyncScope())
        {
            var pageReader = scope2.ServiceProvider.GetRequiredService<IAccountCardPageReader>();

            var page = await pageReader.GetPageAsync(new AccountCardLinePageRequest
            {
                AccountId = cashId,
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 200
            }, CancellationToken.None);

            page.Lines.Should().HaveCount(1);

            var line = page.Lines.Single();
            line.DocumentId.Should().Be(documentId);
            line.AccountId.Should().Be(cashId);
            line.CounterAccountId.Should().Be(revenueId);

            line.Dimensions.IsEmpty.Should().BeFalse();
            line.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == cashDimensionId && x.ValueId == cashValueId);

            line.CounterAccountDimensions.IsEmpty.Should().BeFalse();
            line.CounterAccountDimensions.Items.Should().ContainSingle(x => x.DimensionId == revenueDimensionId && x.ValueId == revenueValueId);

            line.DimensionValueDisplays.Should().ContainKey(cashDimensionId);
            line.DimensionValueDisplays[cashDimensionId].Should().Be("Cash A");

            line.CounterAccountDimensionValueDisplays.Should().ContainKey(revenueDimensionId);
            line.CounterAccountDimensionValueDisplays[revenueDimensionId].Should().Be("Revenue B");
        }

        // High-level report reader: ensures display values are propagated into report lines.
        await using (var scope3 = host.Services.CreateAsyncScope())
        {
            var report = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
                scope3.ServiceProvider,
                cashId,
                ReportingTestHelpers.Period,
                ReportingTestHelpers.Period,
                dimensionScopes: null,
                ct: CancellationToken.None);

            report.Lines.Should().HaveCount(1);

            var rl = report.Lines.Single();
            rl.DocumentId.Should().Be(documentId);
            rl.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == cashDimensionId && x.ValueId == cashValueId);
            rl.DimensionValueDisplays.Should().ContainKey(cashDimensionId);
            rl.DimensionValueDisplays[cashDimensionId].Should().Be("Cash A");
        }
    }

    private static async Task EnsureTypedTablesExistAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {CashTableName} (
                      catalog_id uuid PRIMARY KEY,
                      {DisplayColumn} text NULL
                  );

                  CREATE TABLE IF NOT EXISTS {RevenueTableName} (
                      catalog_id uuid PRIMARY KEY,
                      {DisplayColumn} text NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedAsync(string connectionString, string tableName, Guid id, string name)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {tableName} (catalog_id, {DisplayColumn})
                  VALUES (@id, @name);
                  """;

        await conn.ExecuteAsync(sql, new { id, name });
    }

    private static async Task<(Guid cashId, Guid revenueId)> SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(CashDimensionCode, IsRequired: true)
                }),
            CancellationToken.None);

        var revenueId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(RevenueDimensionCode, IsRequired: true)
                }),
            CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task PostWithDimensionsAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        Guid cashValueId,
        Guid revenueValueId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        var cashDimensionId = DeterministicGuid.Create($"Dimension|{CashDimensionCode.Trim().ToLowerInvariant()}");
        var revenueDimensionId = DeterministicGuid.Create($"Dimension|{RevenueDimensionCode.Trim().ToLowerInvariant()}");

        var debitBag = new DimensionBag(new[] { new DimensionValue(cashDimensionId, cashValueId) });
        var creditBag = new DimensionBag(new[] { new DimensionValue(revenueDimensionId, revenueValueId) });

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    dateUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount,
                    debitBag,
                    creditBag);
            },
            CancellationToken.None);
    }
}
