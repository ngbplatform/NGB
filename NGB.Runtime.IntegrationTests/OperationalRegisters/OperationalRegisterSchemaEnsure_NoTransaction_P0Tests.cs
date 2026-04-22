using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Schema ensure for per-register tables must be safe even without an explicit user transaction.
/// This matters because EnsureSchemaAsync executes multiple DDL statements, and triggers do not support
/// CREATE TRIGGER IF NOT EXISTS.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterSchemaEnsure_NoTransaction_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_Movements_ConcurrentWithoutTransaction_DoesNotThrow_AndCreatesSingleAppendOnlyTrigger()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var resources = Enumerable.Range(1, 15)
            .Select(i => new OperationalRegisterResourceDefinition($"r{i}", $"R{i}", i))
            .ToArray();

        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources);

        // Concurrent callers, each with its own scoped UnitOfWork and connection.
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(async () =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

                // No BeginTransactionAsync on purpose.
                await movements.EnsureSchemaAsync(registerId, CancellationToken.None);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Verify: single append-only trigger exists for the movements table.
        var movementsTable = NGB.OperationalRegisters.OperationalRegisterNaming.MovementsTable("rr");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var triggerCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                WHERE NOT t.tgisinternal
                  AND c.relname = @RelName
                  AND t.tgname LIKE 'trg_opreg_append_only_%';
                """,
                new { RelName = movementsTable },
                cancellationToken: CancellationToken.None));

            triggerCount.Should().Be(1);

            // Verify: a couple of resource columns were created as well (sanity).
            var colCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @RelName
                  AND column_name IN ('r1', 'r15');
                """,
                new { RelName = movementsTable },
                cancellationToken: CancellationToken.None));

            colCount.Should().Be(2);
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

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
