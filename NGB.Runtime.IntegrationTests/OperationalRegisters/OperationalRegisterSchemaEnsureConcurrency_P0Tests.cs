using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: EnsureSchemaAsync uses dynamic DDL + a CREATE TRIGGER DO-block (no IF NOT EXISTS).
/// This must be serialized per register to avoid races when multiple writers hit a new register concurrently.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterSchemaEnsureConcurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MovementsStore_EnsureSchema_ConcurrentCalls_DoNotThrow_AndAppendOnlyTriggerIsSingle()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 30, 12, 0, 0, DateTimeKind.Utc);

        // Arrange: register + a bunch of resources to slow down EnsureSchema (makes the pre-lock race reproducible).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            var defs = Enumerable.Range(1, 15)
                .Select(i => new OperationalRegisterResourceDefinition($"AMOUNT_{i:00}", $"Amount {i:00}", Ordinal: i * 10))
                .ToArray();

            await resRepo.ReplaceAsync(regId, defs, nowUtc, CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Act: hit EnsureSchema concurrently in separate transactions/scopes.
        const int workers = 6;

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task Worker()
        {
            await using var scope = host.Services.CreateAsyncScope();

            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

            if (Interlocked.Increment(ref ready) == workers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            await uow.BeginTransactionAsync(CancellationToken.None);
            await store.EnsureSchemaAsync(regId, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Worker()).ToArray();

        // Release all workers at once.
        await allReady.Task;
        go.TrySetResult();

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        // Assert: table exists and has exactly one append-only trigger.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var table = OperationalRegisterNaming.MovementsTable("rr");

            var exists = await uow.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT to_regclass(@Table) IS NOT NULL;",
                    new { Table = table },
                    cancellationToken: CancellationToken.None));

            exists.Should().BeTrue();

            var trgCount = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM pg_trigger
                    WHERE tgrelid = @Table::regclass
                      AND NOT tgisinternal
                      AND tgname LIKE 'trg_opreg_append_only_%';
                    """,
                    new { Table = table },
                    cancellationToken: CancellationToken.None));

            trgCount.Should().Be(1, "EnsureSchema must create the append-only trigger exactly once even under concurrency");
        }
    }
}
