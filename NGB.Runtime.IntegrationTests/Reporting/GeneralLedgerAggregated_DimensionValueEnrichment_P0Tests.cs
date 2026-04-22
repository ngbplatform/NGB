using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
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
public sealed class GeneralLedgerAggregated_DimensionValueEnrichment_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DimensionCode = "it_cat_dim_gl";
    private const string TableName = "cat_it_cat_dim_gl";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task GeneralLedgerAggregated_AttachesDimensionValueDisplays_ForSelectedSide()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        var valueId = Guid.CreateVersion7();
        await SeedAsync(Fixture.ConnectionString, valueId, "First");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId) = await SeedCoAAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();
            registry.Register(new CatalogTypeMetadata(
                CatalogCode: DimensionCode,
                DisplayName: "IT Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata(TableName, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "it")));
        }

        var documentId = Guid.CreateVersion7();

        await PostWithDimensionAsync(
            host,
            documentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            dimensionCode: DimensionCode,
            valueId: valueId);

        var dimensionId = DeterministicGuid.Create($"Dimension|{DimensionCode.Trim().ToLowerInvariant()}");

        // Low-level detail reader: enrichment should be attached on lines.
        await using (var scope2 = host.Services.CreateAsyncScope())
        {
            var lines = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
                scope2.ServiceProvider,
                cashId,
                ReportingTestHelpers.Period,
                ReportingTestHelpers.Period,
                dimensionScopes: null,
                ct: CancellationToken.None);

            lines.Should().HaveCount(1);

            var line = lines.Single();
            line.DocumentId.Should().Be(documentId);
            line.AccountId.Should().Be(cashId);
            line.CounterAccountId.Should().Be(revenueId);

            line.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueId);
            line.DimensionValueDisplays.Should().ContainKey(dimensionId);
            line.DimensionValueDisplays[dimensionId].Should().Be("First");
        }

        // High-level report reader: display values should be propagated into report lines.
        await using (var scope3 = host.Services.CreateAsyncScope())
        {
            var report = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedReportAsync(
                scope3.ServiceProvider,
                cashId,
                ReportingTestHelpers.Period,
                ReportingTestHelpers.Period,
                dimensionScopes: null,
                ct: CancellationToken.None);

            report.Lines.Should().HaveCount(1);

            var rl = report.Lines.Single();
            rl.DocumentId.Should().Be(documentId);
            rl.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueId);
            rl.DimensionValueDisplays.Should().ContainKey(dimensionId);
            rl.DimensionValueDisplays[dimensionId].Should().Be("First");
        }
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {TableName} (
                      catalog_id uuid PRIMARY KEY,
                      {DisplayColumn} text NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedAsync(string connectionString, Guid id, string name)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {TableName} (catalog_id, {DisplayColumn})
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
                    new AccountDimensionRuleRequest(DimensionCode, IsRequired: true)
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
                IsActive: true),
            CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task PostWithDimensionAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        string dimensionCode,
        Guid valueId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCode.Trim().ToLowerInvariant()}");
        var debitBag = new DimensionBag(new[] { new DimensionValue(dimensionId, valueId) });

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
                    DimensionBag.Empty);
            },
            CancellationToken.None);
    }
}
