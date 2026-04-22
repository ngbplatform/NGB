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

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_AppendOnlyEnforcement_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MovementsTable_ForbidsUpdateAndDelete_AndDoesNotMutateExistingRows()
    {
        var ct = CancellationToken.None;

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Seed register + resources.
        await SeedRegisterAsync(host, registerId, code: "it_rr_append", name: "AppendOnly Test", ct);
        await SeedResourcesAsync(host, registerId, new[]
        {
            new OperationalRegisterResourceDefinition(Code: "amount", Name: "Amount", Ordinal: 1)
        }, ct);

        // Ensure per-register movements table exists.
        await EnsureMovementsSchemaAsync(host, registerId, ct);

        // Insert a movement row.
        await InsertMovementAsync(host, registerId, documentId, occurredAtUtc, ct);

        // Resolve table name and movement_id for assertions.
        var (table, movementId) = await GetFirstMovementRowAsync(host, registerId, ct);

        // Attempt UPDATE should fail via append-only trigger.
        await AssertMutationForbiddenAsync(
            host,
            $"UPDATE {table} SET is_storno = TRUE WHERE movement_id = @Id;",
            new { Id = movementId },
            table,
            ct);

        // Row should remain unchanged.
        (await GetIsStornoAsync(host, table, movementId, ct)).Should().BeFalse();

        // Attempt DELETE should fail via append-only trigger.
        await AssertMutationForbiddenAsync(
            host,
            $"DELETE FROM {table} WHERE movement_id = @Id;",
            new { Id = movementId },
            table,
            ct);

        // Row should still exist.
        (await GetRowCountAsync(host, table, ct)).Should().Be(1);
    }

    private static async Task SeedRegisterAsync(IHost host, Guid registerId, string code, string name, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        await uow.BeginTransactionAsync(ct);
        await registers.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc: DateTime.UtcNow, ct);
        await uow.CommitAsync(ct);
    }

    private static async Task SeedResourcesAsync(IHost host, Guid registerId, IReadOnlyList<OperationalRegisterResourceDefinition> resources, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        await uow.BeginTransactionAsync(ct);
        await repo.ReplaceAsync(registerId, resources, nowUtc: DateTime.UtcNow, ct);
        await uow.CommitAsync(ct);
    }

    private static async Task EnsureMovementsSchemaAsync(IHost host, Guid registerId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        await movements.EnsureSchemaAsync(registerId, ct);
    }

    private static async Task InsertMovementAsync(IHost host, Guid registerId, Guid documentId, DateTime occurredAtUtc, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

        await uow.BeginTransactionAsync(ct);

        var movement = new OperationalRegisterMovement(
            DocumentId: documentId,
            OccurredAtUtc: occurredAtUtc,
            DimensionSetId: Guid.Empty,
            Resources: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 123.45m });

        await store.AppendAsync(registerId, new[] { movement }, ct);

        await uow.CommitAsync(ct);
    }

    private static async Task<(string Table, long MovementId)> GetFirstMovementRowAsync(IHost host, Guid registerId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var reg = await registers.GetByIdAsync(registerId, ct);
        reg.Should().NotBeNull();

        var table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        await uow.EnsureConnectionOpenAsync(ct);
        var id = await uow.Connection.QuerySingleAsync<long>($"SELECT movement_id FROM {table} ORDER BY movement_id LIMIT 1;");
        return (table, id);
    }

    private static async Task<bool> GetIsStornoAsync(IHost host, string table, long movementId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.QuerySingleAsync<bool>(
            $"SELECT is_storno FROM {table} WHERE movement_id = @Id;",
            new { Id = movementId });
    }

    private static async Task<int> GetRowCountAsync(IHost host, string table, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.QuerySingleAsync<int>($"SELECT count(*)::int FROM {table};");
    }

    private static async Task AssertMutationForbiddenAsync(IHost host, string sql, object param, string table, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(ct);

        try
        {
            var act = async () => await uow.Connection.ExecuteAsync(sql, param, transaction: uow.Transaction);
            var ex = await act.Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("55000");
            ex.Which.MessageText.Should().Contain("Append-only table cannot be mutated");
            ex.Which.MessageText.Should().Contain(table);

            await uow.RollbackAsync(ct);
        }
        catch
        {
            // Ensure transaction is not left open/aborted in the test process.
            if (uow.HasActiveTransaction) await uow.RollbackAsync(ct);
            throw;
        }
    }
}
