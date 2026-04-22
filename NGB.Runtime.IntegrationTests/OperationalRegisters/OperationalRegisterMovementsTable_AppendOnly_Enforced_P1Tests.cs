using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P1: per-register movements tables (opreg_*__movements) are append-only.
///
/// We already test that EnsureSchema creates a single append-only trigger under concurrency;
/// this test verifies the *enforcement* behavior: UPDATE/DELETE are rejected by the shared
/// guard function ngb_forbid_mutation_of_append_only_table().
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovementsTable_AppendOnly_Enforced_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAndDelete_AreForbidden_ByAppendOnlyGuard()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(
            host,
            registerId,
            code: "RR",
            name: "Rent Roll",
            resources: [new OperationalRegisterResourceDefinition("r1", "R1", 1)]);

        // Ensure physical schema exists.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
            await movements.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        var documentId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);

        // Insert a movement (INSERT is allowed).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await movements.AppendAsync(
                registerId,
                [new OperationalRegisterMovement(
                    DocumentId: documentId,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["r1"] = 10m })],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Resolve physical movements table name.
        string table;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var registers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var reg = await registers.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();

            table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);
        }

        // Fetch inserted movement_id.
        long movementId;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            movementId = await conn.ExecuteScalarAsync<long>(
                $"SELECT movement_id FROM {table} WHERE document_id = @DocId LIMIT 1;",
                new { DocId = documentId });
        }

        // UPDATE must be rejected.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"UPDATE {table} SET r1 = 999 WHERE movement_id = @Id;",
                    new { Id = movementId },
                    tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            ex.Which.MessageText.Should().Contain("Append-only table cannot be mutated");

            await tx.RollbackAsync(CancellationToken.None);
        }

        // DELETE must be rejected.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"DELETE FROM {table} WHERE movement_id = @Id;",
                    new { Id = movementId },
                    tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            ex.Which.MessageText.Should().Contain("Append-only table cannot be mutated");

            await tx.RollbackAsync(CancellationToken.None);
        }

        // Verify: row still exists and value did not change.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM {table} WHERE movement_id = @Id;",
                new { Id = movementId });

            count.Should().Be(1);

            var r1 = await conn.ExecuteScalarAsync<decimal>(
                $"SELECT r1 FROM {table} WHERE movement_id = @Id;",
                new { Id = movementId });

            r1.Should().Be(10m);
        }
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
