using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// DB-level append-only guard for per-register movements tables.
///
/// Operational Registers use append-only movements + storno semantics; Update/Delete must be forbidden.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_OperationalRegisters_MovementsTables_AppendOnly_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MovementsTable_IsAppendOnly_UpdateAndDeleteAreForbidden()
    {
        var (_, table) = await SeedRegisterWithOneMovementAsync();

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var movementId = await conn.ExecuteScalarAsync<long>($"SELECT movement_id FROM {table} LIMIT 1;");
        movementId.Should().BeGreaterThan(0);

        await AssertAppendOnlyAsync(() => conn.ExecuteAsync(
            $"UPDATE {table} SET occurred_at_utc = occurred_at_utc + interval '1 second' WHERE movement_id = @Id;",
            new { Id = movementId }), table);

        await AssertAppendOnlyAsync(() => conn.ExecuteAsync(
            $"DELETE FROM {table} WHERE movement_id = @Id;",
            new { Id = movementId }), table);
    }

    private async Task<(Guid RegisterId, string MovementsTable)> SeedRegisterWithOneMovementAsync()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var movementsStore = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var codeNorm = $"it_opreg_mov_guard_{suffix}";
        var registerId = await management.UpsertAsync(codeNorm, codeNorm);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await movementsStore.EnsureSchemaAsync(registerId, ct);

            await movementsStore.AppendAsync(
                registerId,
                [
                    new OperationalRegisterMovement(
                        DocumentId: Guid.CreateVersion7(),
                        OccurredAtUtc: DateTime.UtcNow,
                        DimensionSetId: Guid.Empty,
                        Resources: new Dictionary<string, decimal>(StringComparer.Ordinal))
                ],
                ct);
        });

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var tableCode = await conn.ExecuteScalarAsync<string>(
            "SELECT table_code FROM operational_registers WHERE register_id = @Id;",
            new { Id = registerId });

        tableCode.Should().NotBeNullOrWhiteSpace();

        var table = OperationalRegisterNaming.MovementsTable(tableCode);
        return (registerId, table);
    }

    private static async Task AssertAppendOnlyAsync(Func<Task> act, string table)
    {
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.Message.Should().Contain("Append-only table cannot be mutated");
        ex.Which.Message.Should().Contain(table);
    }
}
