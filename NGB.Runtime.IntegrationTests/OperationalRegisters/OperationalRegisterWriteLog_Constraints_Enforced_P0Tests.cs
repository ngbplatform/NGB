using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: strict DB constraints for operational_register_write_state.
/// This log underpins idempotency and must reject invalid operation/timestamp combinations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteLog_Constraints_Enforced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InvalidRowsAreRejectedByCheckConstraints()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateRegisterAsync(host, "rr_" + Guid.CreateVersion7().ToString("N")[..8]);
        var documentId = Guid.CreateVersion7();
        await SeedDraftDocAsync(host, documentId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var startedAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // 1) operation must be 1..3
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_write_state (register_id, document_id, operation, started_at_utc)
                    VALUES (@R, @D, @Op, @Started);
                    """,
                    new { R = registerId, D = documentId, Op = 0, Started = startedAt }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_write_log_operation");
        }

        // 2) completed_at_utc must be >= started_at_utc
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_write_state (register_id, document_id, operation, started_at_utc, completed_at_utc)
                    VALUES (@R, @D, @Op, @Started, @Completed);
                    """,
                    new
                    {
                        R = registerId,
                        D = documentId,
                        Op = (short)OperationalRegisterWriteOperation.Post,
                        Started = startedAt,
                        Completed = startedAt.AddSeconds(-1)
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_write_log_completed_after_started");
        }
    }

    [Fact]
    public async Task ValidRowIsAccepted_AndPrimaryKeyIsEnforced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = await CreateRegisterAsync(host, "rr_" + Guid.CreateVersion7().ToString("N")[..8]);

        var documentId = Guid.CreateVersion7();
        await SeedDraftDocAsync(host, documentId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var startedAt = new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc);

        await conn.ExecuteAsync(
            """
            INSERT INTO operational_register_write_state (register_id, document_id, operation, started_at_utc, completed_at_utc)
            VALUES (@R, @D, @Op, @Started, NULL);
            """,
            new
            {
                R = registerId,
                D = documentId,
                Op = (short)OperationalRegisterWriteOperation.Post,
                Started = startedAt
            });

        // PK (register_id, document_id, operation)
        var ex = await FluentActions
            .Invoking(() => conn.ExecuteAsync(
                """
                INSERT INTO operational_register_write_state (register_id, document_id, operation, started_at_utc)
                VALUES (@R, @D, @Op, @Started);
                """,
                new
                {
                    R = registerId,
                    D = documentId,
                    Op = (short)OperationalRegisterWriteOperation.Post,
                    Started = startedAt.AddMinutes(1)
                }))
            .Should().ThrowAsync<PostgresException>();

        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("pk_operational_register_write_state");
    }

    private static async Task<Guid> CreateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: "IT Register", CancellationToken.None);
    }

    private static async Task SeedDraftDocAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc);
        var dateUtc = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }
}
