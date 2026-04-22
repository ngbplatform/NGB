using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Database-level hard constraints must protect the platform even if a bug slips through
/// application-layer validators.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_Documents_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Documents_StatusEnum_IsEnforcedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO documents(
                    id, type_code, number, date_utc,
                    status, posted_at_utc, marked_for_deletion_at_utc,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @Id, 'IT', NULL, @DateUtc,
                    99, NULL, NULL,
                    @CreatedAt, @UpdatedAt
                );
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    DateUtc = T0,
                    CreatedAt = T0,
                    UpdatedAt = T0
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_documents_status");
    }

    [Fact]
    public async Task Documents_PostedState_IsEnforcedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // status=2 requires posted_at_utc != NULL and marked_for_deletion_at_utc == NULL
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO documents(
                    id, type_code, number, date_utc,
                    status, posted_at_utc, marked_for_deletion_at_utc,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @Id, 'IT', NULL, @DateUtc,
                    2, NULL, NULL,
                    @CreatedAt, @UpdatedAt
                );
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    DateUtc = T0,
                    CreatedAt = T0,
                    UpdatedAt = T0
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_documents_posted_state");
    }

    [Fact]
    public async Task Documents_MarkedForDeletionState_IsEnforcedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // status=3 requires marked_for_deletion_at_utc != NULL and posted_at_utc == NULL
        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO documents(
                    id, type_code, number, date_utc,
                    status, posted_at_utc, marked_for_deletion_at_utc,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    @Id, 'IT', NULL, @DateUtc,
                    3, NULL, NULL,
                    @CreatedAt, @UpdatedAt
                );
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    DateUtc = T0,
                    CreatedAt = T0,
                    UpdatedAt = T0
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_documents_marked_for_deletion_state");
    }

    [Fact]
    public async Task Documents_DraftRow_WithUtcTimestamps_IsAcceptedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO documents(
                id, type_code, number, date_utc,
                status, posted_at_utc, marked_for_deletion_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, 'IT', '0001', @DateUtc,
                1, NULL, NULL,
                @CreatedAt, @UpdatedAt
            );
            """,
            new
            {
                Id = id,
                DateUtc = T0,
                CreatedAt = T0,
                UpdatedAt = T0
            });

        var row = await conn.QuerySingleAsync<(Guid Id, short Status)>(
            "SELECT id AS Id, status AS Status FROM documents WHERE id=@Id;",
            new { Id = id });

        row.Id.Should().Be(id);
        row.Status.Should().Be(1);
    }
}
