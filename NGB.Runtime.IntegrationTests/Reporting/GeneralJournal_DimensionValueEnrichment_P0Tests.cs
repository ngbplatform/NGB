using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
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
public sealed class GeneralJournal_DimensionValueEnrichment_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DimensionCode = "it_cat_dim";
    private const string TableName = "cat_it_cat_dim";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task GeneralJournalReport_AttachesDimensionValueDisplays_ForCatalogBackedDimension()
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

        await using var scope2 = host.Services.CreateAsyncScope();
        var report = scope2.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page = await report.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            PageSize = 200,
            DocumentId = documentId
        }, CancellationToken.None);

        page.Lines.Should().HaveCount(1);

        var line = page.Lines.Single();
        line.DocumentId.Should().Be(documentId);
        line.DebitAccountId.Should().Be(cashId);
        line.CreditAccountId.Should().Be(revenueId);

        line.DebitDimensions.IsEmpty.Should().BeFalse();

        var dimensionCodeNorm = DimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");

        line.DebitDimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueId);
        line.DebitDimensionValueDisplays.Should().ContainKey(dimensionId);
        line.DebitDimensionValueDisplays[dimensionId].Should().Be("First");

        line.CreditDimensions.IsEmpty.Should().BeTrue();
        line.CreditDimensionValueDisplays.Should().BeEmpty();
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

        var dimensionCodeNorm = dimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");
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
