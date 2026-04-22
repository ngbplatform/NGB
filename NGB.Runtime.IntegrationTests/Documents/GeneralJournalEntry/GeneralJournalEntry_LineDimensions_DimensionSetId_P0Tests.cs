using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_LineDimensions_DimensionSetId_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task PostApproved_WhenLinesHaveDimensions_PersistsLineDimensionSetId_AndWritesRegisterRowsWithThoseIds()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, dimId) = await EnsureAccountsWithBuildingDimensionAsync(host);

        // Use distinct value IDs to prove per-side DimensionSetId is preserved.
        var buildingA = Guid.CreateVersion7();
        var buildingB = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var docDateUtc = new DateTime(2026, 01, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            docId,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "DIMSET_TEST",
                Memo: "DimensionSetId on lines",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            docId,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 100m,
                    Memo: "D",
                    Dimensions: new[] { new DimensionValue(dimId, buildingA) }),

                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 100m,
                    Memo: "C",
                    Dimensions: new[] { new DimensionValue(dimId, buildingB) }),
            },
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
        await gje.ApproveAsync(docId, approvedBy: "u2", ct: CancellationToken.None);
        await gje.PostApprovedAsync(docId, postedBy: "u2", ct: CancellationToken.None);

        // Sanity: common document must become Posted
        var docs = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Documents.IDocumentRepository>();
        (await docs.GetAsync(docId, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);

        // Assert: lines have persisted DimensionSetId (non-empty, per-side distinct).
        Guid debitLineSetId;
        Guid creditLineSetId;

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
SELECT line_no, dimension_set_id
FROM doc_general_journal_entry__lines
WHERE document_id = @d
ORDER BY line_no", conn);
            cmd.Parameters.AddWithValue("d", docId);

            var rows = new List<(int lineNo, Guid setId)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt32(0), reader.GetGuid(1)));

            rows.Should().HaveCount(2);

            debitLineSetId = rows.Single(x => x.lineNo == 1).setId;
            creditLineSetId = rows.Single(x => x.lineNo == 2).setId;
        }

        debitLineSetId.Should().NotBe(Guid.Empty);
        creditLineSetId.Should().NotBe(Guid.Empty);
        debitLineSetId.Should().NotBe(creditLineSetId);

        // Assert: register rows preserve those set ids.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
SELECT debit_dimension_set_id, credit_dimension_set_id
FROM accounting_register_main
WHERE document_id = @d", conn);
            cmd.Parameters.AddWithValue("d", docId);

            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();

            var debitSet = reader.GetGuid(0);
            var creditSet = reader.GetGuid(1);

            debitSet.Should().Be(debitLineSetId);
            creditSet.Should().Be(creditLineSetId);
        }

        // Assert: platform mapping rows exist + correct set items.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // Both sets must exist
            await using (var cmd = new NpgsqlCommand(@"
SELECT COUNT(*)::int
FROM platform_dimension_sets
WHERE dimension_set_id = ANY(@ids)", conn))
            {
                cmd.Parameters.AddWithValue("ids", new[] { debitLineSetId, creditLineSetId });
                (await cmd.ExecuteScalarAsync())!.Should().Be(2);
            }

            // Each set must have the expected (dimension_id,value_id)
            async Task AssertItem(Guid setId, Guid valueId)
            {
                await using var cmd = new NpgsqlCommand(@"
SELECT COUNT(*)::int
FROM platform_dimension_set_items
WHERE dimension_set_id = @s AND dimension_id = @d AND value_id = @v", conn);
                cmd.Parameters.AddWithValue("s", setId);
                cmd.Parameters.AddWithValue("d", dimId);
                cmd.Parameters.AddWithValue("v", valueId);

                (await cmd.ExecuteScalarAsync())!.Should().Be(1);
            }

            await AssertItem(debitLineSetId, buildingA);
            await AssertItem(creditLineSetId, buildingB);
        }
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid dimId)> EnsureAccountsWithBuildingDimensionAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // DimensionRules are persisted and also upsert platform_dimensions (dimension_id is deterministic per code).
        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: [new AccountDimensionRuleRequest(DimensionCode: "building", IsRequired: true, Ordinal: 1)
                ]),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: [new AccountDimensionRuleRequest(DimensionCode: "building", IsRequired: true, Ordinal: 1)
                ]),
            CancellationToken.None);

        // Read back the dimension_id via CoA snapshot.
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await coa.GetAsync(CancellationToken.None);

        var dimId = chart.Get(cashId).DimensionRules.Single().DimensionId;
        dimId.Should().NotBe(Guid.Empty);

        return (cashId, revenueId, dimId);
    }
}
