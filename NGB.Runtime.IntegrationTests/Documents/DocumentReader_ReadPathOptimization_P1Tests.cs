using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Persistence.Common;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(SchemaPostgresCollection.Name)]
public sealed class DocumentReader_ReadPathOptimization_P1Tests(SchemaPostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_read_path";
    private const string HeadTable = "doc_it_doc_read_path";
    private const string DisplayColumn = "display";

    [Fact]
    public async Task CombinedPageAsync_WithoutHeadCriteria_PagesAcrossHeadRowsAndNullOrMissingHeadRows()
    {
        await EnsureCleanDocumentTypeAsync(Fixture.ConnectionString);

        var alphaId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var betaId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var nullDisplayId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var missingHeadId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        await SeedDocumentAsync(Fixture.ConnectionString, alphaId, "RP-001", DocumentStatus.Draft);
        await SeedDocumentAsync(Fixture.ConnectionString, betaId, "RP-002", DocumentStatus.Draft);
        await SeedDocumentAsync(Fixture.ConnectionString, nullDisplayId, "RP-003", DocumentStatus.Draft);
        await SeedDocumentAsync(Fixture.ConnectionString, missingHeadId, "RP-004", DocumentStatus.Draft);
        await SeedHeadAsync(Fixture.ConnectionString, alphaId, "Alpha", 10m);
        await SeedHeadAsync(Fixture.ConnectionString, betaId, "Beta", 20m);
        await SeedHeadAsync(Fixture.ConnectionString, nullDisplayId, null, 30m);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentReader>();
        var head = HeadDescriptor();
        var query = new DocumentQuery(Search: null, Filters: []);

        var combined = reader.Should().BeAssignableTo<IDocumentCombinedPageReader>().Subject;
        var result = await combined.GetPageWithTotalAsync(
            head, query, offset: 2, limit: 10, CancellationToken.None);
        var page = result.Rows;

        result.Total.Should().Be(4);
        page.Select(x => x.Id).Should().Equal(nullDisplayId, missingHeadId);

        page[0].Display.Should().Be(nullDisplayId.ToString("D"));
        page[0].Fields[DisplayColumn].Should().BeNull();
        page[0].Fields["amount"].Should().Be(30m);

        page[1].Display.Should().Be(missingHeadId.ToString("D"));
        page[1].Fields[DisplayColumn].Should().BeNull();
        page[1].Fields["amount"].Should().BeNull();
    }

    [Fact]
    public async Task CombinedPageAsync_WithoutHeadCriteria_PreservesActiveSoftDeleteFilter()
    {
        await EnsureCleanDocumentTypeAsync(Fixture.ConnectionString);

        var deletedId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var activeHeadId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var activeMissingHeadId = Guid.Parse("00000000-0000-0000-0000-000000000013");

        await SeedDocumentAsync(Fixture.ConnectionString, deletedId, "RP-011", DocumentStatus.MarkedForDeletion);
        await SeedDocumentAsync(Fixture.ConnectionString, activeHeadId, "RP-012", DocumentStatus.Draft);
        await SeedDocumentAsync(Fixture.ConnectionString, activeMissingHeadId, "RP-013", DocumentStatus.Draft);
        await SeedHeadAsync(Fixture.ConnectionString, deletedId, "Aardvark deleted", 11m);
        await SeedHeadAsync(Fixture.ConnectionString, activeHeadId, "Alpha active", 12m);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentReader>();
        var head = HeadDescriptor();
        var query = new DocumentQuery(Search: null, Filters: [])
        {
            SoftDeleteFilterMode = SoftDeleteFilterMode.Active
        };

        var combined = reader.Should().BeAssignableTo<IDocumentCombinedPageReader>().Subject;
        var result = await combined.GetPageWithTotalAsync(
            head, query, offset: 0, limit: 10, CancellationToken.None);
        var page = result.Rows;

        result.Total.Should().Be(2);
        page.Select(x => x.Id).Should().Equal(activeHeadId, activeMissingHeadId);
        page.Should().NotContain(x => x.Id == deletedId);
    }

    private static DocumentHeadDescriptor HeadDescriptor()
        => new(
            TypeCode,
            HeadTable,
            DisplayColumn,
            [
                new DocumentHeadColumn(DisplayColumn, ColumnType.String),
                new DocumentHeadColumn("amount", ColumnType.Decimal)
            ]);

    private static async Task EnsureCleanDocumentTypeAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {HeadTable}
             (
                 document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                 display text NULL,
                 amount numeric(18, 2) NULL
             );

             DELETE FROM documents
              WHERE type_code = @typeCode;
             """,
            new { typeCode = TypeCode });
    }

    private static async Task SeedDocumentAsync(
        string connectionString,
        Guid id,
        string number,
        DocumentStatus status)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;
        var markedForDeletionAtUtc = status == DocumentStatus.MarkedForDeletion ? nowUtc : (DateTime?)null;
        var postedAtUtc = status == DocumentStatus.Posted ? nowUtc : (DateTime?)null;

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
                               @nowUtc,
                               @status,
                               @postedAtUtc,
                               @markedForDeletionAtUtc,
                               @nowUtc,
                               @nowUtc
                           );
                           """;

        await conn.ExecuteAsync(sql, new
        {
            id,
            typeCode = TypeCode,
            number,
            nowUtc,
            status = (short)status,
            postedAtUtc,
            markedForDeletionAtUtc
        });
    }

    private static async Task SeedHeadAsync(
        string connectionString,
        Guid id,
        string? display,
        decimal amount)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            $"""
             INSERT INTO {HeadTable}(document_id, display, amount)
             VALUES (@id, @display, @amount);
             """,
            new { id, display, amount });
    }
}
