using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class DimensionValueEnrichment_DocumentFallback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DimensionCode = "it_doc_dim";

    [Fact]
    public async Task GeneralJournalReport_UsesDocumentFallback_WhenDimensionIsNotCatalogBacked()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId) = await SeedCoAAsync(host);

        // Seed a document row that will be referenced as a dimension ValueId.
        var valueDocumentId = Guid.CreateVersion7();
        await SeedDocumentAsync(Fixture.ConnectionString, valueDocumentId, typeCode: "it_doc", number: "DOC-1");

        var postingDocumentId = Guid.CreateVersion7();

        await PostWithDimensionAsync(
            host,
            postingDocumentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            dimensionCode: DimensionCode,
            valueId: valueDocumentId);

        await using var scope2 = host.Services.CreateAsyncScope();
        var report = scope2.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page = await report.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            PageSize = 200,
            DocumentId = postingDocumentId
        }, CancellationToken.None);

        page.Lines.Should().HaveCount(1);

        var line = page.Lines.Single();
        line.DocumentId.Should().Be(postingDocumentId);
        line.DebitAccountId.Should().Be(cashId);
        line.CreditAccountId.Should().Be(revenueId);

        var dimensionCodeNorm = DimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");

        line.DebitDimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueDocumentId);

        // Catalog is not registered for DimensionCode, so enrichment must fallback to documents table:
        // BuildDocumentDisplay uses type_code when registry doesn't know it.
        line.DebitDimensionValueDisplays.Should().ContainKey(dimensionId);
        line.DebitDimensionValueDisplays[dimensionId].Should().Be("it_doc DOC-1");

        line.CreditDimensions.IsEmpty.Should().BeTrue();
        line.CreditDimensionValueDisplays.Should().BeEmpty();
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
