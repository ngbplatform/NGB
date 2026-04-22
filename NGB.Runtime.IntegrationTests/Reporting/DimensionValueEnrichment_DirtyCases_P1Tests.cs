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
public sealed class DimensionValueEnrichment_DirtyCases_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DisplayColumn = "name";

    private const string DeletedCatalogDimensionCode = "it_cat_dim_deleted";
    private const string DeletedCatalogTableName = "cat_it_cat_dim_deleted";

    private const string MixedCatalogDimensionCode = "it_cat_dim_mix";
    private const string MixedCatalogTableName = "cat_it_cat_dim_mix";

    private const string MixedDocumentDimensionCode = "it_doc_dim_mix";
    private const string MixedUnknownDimensionCode = "it_unknown_dim_mix";

    [Fact]
    public async Task GeneralJournalReport_UsesShortGuidFallback_WhenDimensionIsSoftDeleted_EvenIfCatalogBacked()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, DeletedCatalogTableName);

        var valueId = Guid.CreateVersion7();
        await SeedCatalogValueAsync(Fixture.ConnectionString, DeletedCatalogTableName, valueId, "First");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host, debitDimensionCodes: [DeletedCatalogDimensionCode]);

        await RegisterCatalogAsync(host, DeletedCatalogDimensionCode, DeletedCatalogTableName, DisplayColumn);

        var documentId = Guid.CreateVersion7();
        await PostWithDebitDimensionsAsync(
            host,
            documentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            debitDimensions: new[]
            {
                Dim(DeletedCatalogDimensionCode, valueId)
            });

        // Sanity: before deletion, catalog enrichment wins.
        var lineBefore = await GetSingleGeneralJournalLineAsync(host, documentId);
        var dimId = DimensionId(DeletedCatalogDimensionCode);
        lineBefore.DebitDimensionValueDisplays.Should().ContainKey(dimId);
        lineBefore.DebitDimensionValueDisplays[dimId].Should().Be("First");

        // Soft-delete the dimension definition: enrichment should lose the code->catalog mapping.
        await SoftDeleteDimensionAsync(Fixture.ConnectionString, dimId);

        var expectedShortGuid = valueId.ToString("N")[..8];

        var lineAfter = await GetSingleGeneralJournalLineAsync(host, documentId);
        lineAfter.DebitDimensions.Items.Should().ContainSingle(x => x.DimensionId == dimId && x.ValueId == valueId);
        lineAfter.DebitDimensionValueDisplays.Should().ContainKey(dimId);
        lineAfter.DebitDimensionValueDisplays[dimId].Should().Be(expectedShortGuid);
    }

    [Fact]
    public async Task GeneralJournalReport_UsesDocumentFallback_WhenDimensionIsSoftDeleted_AndValueIdIsDocument()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, DeletedCatalogTableName);

        // The same value id exists in both catalog table and documents.
        // Before dimension delete: catalog wins.
        // After dimension delete: catalog cannot be used, so document fallback wins.
        var valueDocumentId = Guid.CreateVersion7();
        await SeedCatalogValueAsync(Fixture.ConnectionString, DeletedCatalogTableName, valueDocumentId, "Catalog Name");
        await SeedDocumentAsync(Fixture.ConnectionString, valueDocumentId, typeCode: "it_doc", number: "DOC-2");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host, debitDimensionCodes: [DeletedCatalogDimensionCode]);
        await RegisterCatalogAsync(host, DeletedCatalogDimensionCode, DeletedCatalogTableName, DisplayColumn);

        var postingDocumentId = Guid.CreateVersion7();
        await PostWithDebitDimensionsAsync(
            host,
            postingDocumentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            debitDimensions: new[]
            {
                Dim(DeletedCatalogDimensionCode, valueDocumentId)
            });

        var dimId = DimensionId(DeletedCatalogDimensionCode);

        var lineBefore = await GetSingleGeneralJournalLineAsync(host, postingDocumentId);
        lineBefore.DebitDimensionValueDisplays.Should().ContainKey(dimId);
        lineBefore.DebitDimensionValueDisplays[dimId].Should().Be("Catalog Name");

        await SoftDeleteDimensionAsync(Fixture.ConnectionString, dimId);

        var lineAfter = await GetSingleGeneralJournalLineAsync(host, postingDocumentId);
        lineAfter.DebitDimensionValueDisplays.Should().ContainKey(dimId);
        lineAfter.DebitDimensionValueDisplays[dimId].Should().Be("it_doc DOC-2");
    }

    [Fact]
    public async Task GeneralJournalReport_ResolvesMixedBatch_Catalog_Document_And_Unknown_InOneLine()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, MixedCatalogTableName);

        var catalogValueId = Guid.CreateVersion7();
        await SeedCatalogValueAsync(Fixture.ConnectionString, MixedCatalogTableName, catalogValueId, "Cat A");

        var docValueId = Guid.CreateVersion7();
        await SeedDocumentAsync(Fixture.ConnectionString, docValueId, typeCode: "it_doc_mix", number: "DOC-777");

        var unknownValueId = Guid.CreateVersion7();
        var expectedUnknownDisplay = unknownValueId.ToString("N")[..8];

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(
            host,
            debitDimensionCodes: [MixedCatalogDimensionCode, MixedDocumentDimensionCode, MixedUnknownDimensionCode]);

        await RegisterCatalogAsync(host, MixedCatalogDimensionCode, MixedCatalogTableName, DisplayColumn);

        var postingDocumentId = Guid.CreateVersion7();
        await PostWithDebitDimensionsAsync(
            host,
            postingDocumentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            debitDimensions: new[]
            {
                Dim(MixedCatalogDimensionCode, catalogValueId),
                Dim(MixedDocumentDimensionCode, docValueId),
                Dim(MixedUnknownDimensionCode, unknownValueId)
            });

        var line = await GetSingleGeneralJournalLineAsync(host, postingDocumentId);

        var catDimId = DimensionId(MixedCatalogDimensionCode);
        var docDimId = DimensionId(MixedDocumentDimensionCode);
        var unkDimId = DimensionId(MixedUnknownDimensionCode);

        line.DebitDimensions.Items.Should().Contain(x => x.DimensionId == catDimId && x.ValueId == catalogValueId);
        line.DebitDimensions.Items.Should().Contain(x => x.DimensionId == docDimId && x.ValueId == docValueId);
        line.DebitDimensions.Items.Should().Contain(x => x.DimensionId == unkDimId && x.ValueId == unknownValueId);

        line.DebitDimensionValueDisplays.Should().ContainKey(catDimId);
        line.DebitDimensionValueDisplays[catDimId].Should().Be("Cat A");

        line.DebitDimensionValueDisplays.Should().ContainKey(docDimId);
        line.DebitDimensionValueDisplays[docDimId].Should().Be("it_doc_mix DOC-777");

        line.DebitDimensionValueDisplays.Should().ContainKey(unkDimId);
        line.DebitDimensionValueDisplays[unkDimId].Should().Be(expectedUnknownDisplay);

        line.CreditDimensions.IsEmpty.Should().BeTrue();
        line.CreditDimensionValueDisplays.Should().BeEmpty();
    }

    private static async Task RegisterCatalogAsync(IHost host, string catalogCode, string tableName, string displayColumn)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();

        registry.Register(new CatalogTypeMetadata(
            CatalogCode: catalogCode,
            DisplayName: $"IT {catalogCode}",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(tableName, displayColumn),
            Version: new CatalogMetadataVersion(1, "it")));
    }

    private static async Task SeedCoAAsync(IHost host, IReadOnlyList<string> debitDimensionCodes)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var rules = debitDimensionCodes
            .Select(c => new AccountDimensionRuleRequest(c, IsRequired: true))
            .ToArray();

        _ = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: rules),
            CancellationToken.None);

        _ = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);
    }

    private static async Task PostWithDebitDimensionsAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        IReadOnlyCollection<DimensionValue> debitDimensions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        var debitBag = new DimensionBag(debitDimensions);

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

    private static async Task<GeneralJournalLine> GetSingleGeneralJournalLineAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page = await report.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            PageSize = 200,
            DocumentId = documentId
        }, CancellationToken.None);

        page.Lines.Should().HaveCount(1);
        return page.Lines.Single();
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {tableName} (
                      catalog_id uuid PRIMARY KEY,
                      {DisplayColumn} text NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedCatalogValueAsync(string connectionString, string tableName, Guid id, string name)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {tableName} (catalog_id, {DisplayColumn})
                  VALUES (@id, @name);
                  """;

        await conn.ExecuteAsync(sql, new { id, name });
    }

    private static async Task SeedDocumentAsync(string connectionString, Guid id, string typeCode, string number)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var dateUtc = ReportingTestHelpers.Day15Utc;

        const string sql = """
                           INSERT INTO documents (
                               id,
                               type_code,
                               number,
                               date_utc,
                               status,
                               posted_at_utc,
                               marked_for_deletion_at_utc,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @id,
                               @typeCode,
                               @number,
                               @dateUtc,
                               1,
                               NULL,
                               NULL,
                               @dateUtc,
                               @dateUtc
                           );
                           """;

        await conn.ExecuteAsync(sql, new { id, typeCode, number, dateUtc });
    }

    private static async Task SoftDeleteDimensionAsync(string connectionString, Guid dimensionId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           UPDATE platform_dimensions
                           SET is_deleted = TRUE
                           WHERE dimension_id = @dimensionId;
                           """;

        await conn.ExecuteAsync(sql, new { dimensionId });
    }

    private static Guid DimensionId(string code)
    {
        var norm = code.Trim().ToLowerInvariant();
        return DeterministicGuid.Create($"Dimension|{norm}");
    }

    private static DimensionValue Dim(string code, Guid valueId)
        => new(DimensionId(code), valueId);
}
