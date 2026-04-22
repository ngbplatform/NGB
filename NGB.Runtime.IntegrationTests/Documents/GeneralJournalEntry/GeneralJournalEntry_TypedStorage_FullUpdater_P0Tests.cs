using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_TypedStorage_FullUpdater_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpdateDraftViaDocumentDraftService_TouchesTypedUpdatedAt_UsingFullUpdaterHook()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                number: null,
                dateUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        await BackdateTimestampsAsync(Fixture.ConnectionString, docId, T0);

        var (doc0, header0) = await LoadDocAndHeaderAsync(host, docId);
        doc0.CreatedAtUtc.Should().Be(T0);
        doc0.UpdatedAtUtc.Should().Be(T0);
        header0.CreatedAtUtc.Should().Be(T0);
        header0.UpdatedAtUtc.Should().Be(T0);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            await drafts.UpdateDraftAsync(
                documentId: docId,
                number: "GJE-TEST",
                dateUtc: null,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        var (doc1, header1) = await LoadDocAndHeaderAsync(host, docId);

        doc1.CreatedAtUtc.Should().Be(T0, "CreatedAtUtc must be immutable");
        header1.CreatedAtUtc.Should().Be(T0, "typed header CreatedAtUtc must be immutable");

        doc1.UpdatedAtUtc.Should().BeAfter(T0);
        header1.UpdatedAtUtc.Should().Be(doc1.UpdatedAtUtc, "typed storage full-updater hook synchronizes the timestamp");
        doc1.Number.Should().Be("GJE-TEST");
    }

    private static async Task BackdateTimestampsAsync(string connectionString, Guid documentId, DateTime tsUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            UPDATE documents
            SET created_at_utc = @Ts, updated_at_utc = @Ts
            WHERE id = @Id;

            UPDATE doc_general_journal_entry
            SET created_at_utc = @Ts, updated_at_utc = @Ts
            WHERE document_id = @Id;
            """,
            new { Id = documentId, Ts = tsUtc });
    }

    private static async Task<(DocumentRecord doc, GeneralJournalEntryHeaderRecord header)> LoadDocAndHeaderAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

        var doc = await docs.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull();

        var header = await gje.GetHeaderAsync(documentId, CancellationToken.None);
        header.Should().NotBeNull();

        return (doc!, header!);
    }
}
