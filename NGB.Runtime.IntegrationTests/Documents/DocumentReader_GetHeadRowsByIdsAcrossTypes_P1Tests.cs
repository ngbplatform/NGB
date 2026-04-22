using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentReader_GetHeadRowsByIdsAcrossTypes_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeA = "it_doc_head_batch_a";
    private const string TypeB = "it_doc_head_batch_b";
    private const string HeadTableA = "doc_it_doc_head_batch_a";
    private const string HeadTableB = "doc_it_doc_head_batch_b";

    [Fact]
    public async Task GetHeadRowsByIdsAcrossTypesAsync_ReturnsTypedFields_FromDifferentHeadTables()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await EnsureTablesAsync(Fixture.ConnectionString);

        var idA = Guid.CreateVersion7();
        var idB = Guid.CreateVersion7();
        var relatedLeaseId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 4, 1, 12, 30, 0, DateTimeKind.Utc);

        await SeedDocumentAsync(Fixture.ConnectionString, idA, TypeA, "A-001", DocumentStatus.Posted);
        await SeedDocumentAsync(Fixture.ConnectionString, idB, TypeB, "B-001", DocumentStatus.Draft);
        await SeedHeadAAsync(Fixture.ConnectionString, idA, "Charge A", 123.45m, new DateOnly(2026, 4, 1), true);
        await SeedHeadBAsync(Fixture.ConnectionString, idB, "Lease B", relatedLeaseId, occurredAtUtc);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentReader>();

        var rows = await reader.GetHeadRowsByIdsAcrossTypesAsync(
            [
                new DocumentHeadDescriptor(
                    TypeA,
                    HeadTableA,
                    "display",
                    [
                        new DocumentHeadColumn("display", ColumnType.String),
                        new DocumentHeadColumn("amount", ColumnType.Decimal),
                        new DocumentHeadColumn("service_date", ColumnType.Date),
                        new DocumentHeadColumn("priority", ColumnType.Boolean)
                    ]),
                new DocumentHeadDescriptor(
                    TypeB,
                    HeadTableB,
                    "display",
                    [
                        new DocumentHeadColumn("display", ColumnType.String),
                        new DocumentHeadColumn("lease_id", ColumnType.Guid),
                        new DocumentHeadColumn("occurred_at_utc", ColumnType.DateTimeUtc)
                    ])
            ],
            [idB, idA],
            CancellationToken.None);

        rows.Should().HaveCount(2);

        var rowA = rows.Single(x => x.Id == idA);
        rowA.Status.Should().Be(DocumentStatus.Posted);
        rowA.Display.Should().Be("Charge A");
        rowA.Number.Should().Be("A-001");
        rowA.Fields["display"].Should().Be("Charge A");
        rowA.Fields["amount"].Should().Be(123.45m);
        rowA.Fields["service_date"].Should().Be(new DateOnly(2026, 4, 1));
        rowA.Fields["priority"].Should().Be(true);

        var rowB = rows.Single(x => x.Id == idB);
        rowB.Status.Should().Be(DocumentStatus.Draft);
        rowB.Display.Should().Be("Lease B");
        rowB.Number.Should().Be("B-001");
        rowB.Fields["display"].Should().Be("Lease B");
        rowB.Fields["lease_id"].Should().Be(relatedLeaseId);
        rowB.Fields["occurred_at_utc"].Should().Be(occurredAtUtc);
    }

    private static async Task EnsureTablesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {HeadTableA}
             (
                 document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                 display text NULL,
                 amount numeric(18,2) NULL,
                 service_date date NULL,
                 priority boolean NULL
             );

             CREATE TABLE IF NOT EXISTS {HeadTableB}
             (
                 document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                 display text NULL,
                 lease_id uuid NULL,
                 occurred_at_utc timestamptz NULL
             );
             """);
    }

    private static async Task SeedDocumentAsync(
        string connectionString,
        Guid id,
        string typeCode,
        string number,
        DocumentStatus status)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;
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
                               NULL,
                               @nowUtc,
                               @nowUtc
                           )
                           ON CONFLICT (id) DO UPDATE SET
                               type_code = EXCLUDED.type_code,
                               number = EXCLUDED.number,
                               status = EXCLUDED.status,
                               posted_at_utc = EXCLUDED.posted_at_utc,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        await conn.ExecuteAsync(sql, new { id, typeCode, number, nowUtc, postedAtUtc, status = (short)status });
    }

    private static async Task SeedHeadAAsync(
        string connectionString,
        Guid id,
        string display,
        decimal amount,
        DateOnly serviceDate,
        bool priority)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            $"""
             INSERT INTO {HeadTableA}(document_id, display, amount, service_date, priority)
             VALUES (@id, @display, @amount, @serviceDate, @priority)
             ON CONFLICT (document_id) DO UPDATE SET
                 display = EXCLUDED.display,
                 amount = EXCLUDED.amount,
                 service_date = EXCLUDED.service_date,
                 priority = EXCLUDED.priority;
             """,
            new { id, display, amount, serviceDate, priority });
    }

    private static async Task SeedHeadBAsync(
        string connectionString,
        Guid id,
        string display,
        Guid leaseId,
        DateTime occurredAtUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            $"""
             INSERT INTO {HeadTableB}(document_id, display, lease_id, occurred_at_utc)
             VALUES (@id, @display, @leaseId, @occurredAtUtc)
             ON CONFLICT (document_id) DO UPDATE SET
                 display = EXCLUDED.display,
                 lease_id = EXCLUDED.lease_id,
                 occurred_at_utc = EXCLUDED.occurred_at_utc;
             """,
            new { id, display, leaseId, occurredAtUtc });
    }
}
