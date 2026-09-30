using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.UnitOfWork;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.OperationalRegisters;
using Npgsql;

// Test-only child process. Credentials travel over redirected stdin, never argv/stdout.
// Checkpoints bracket the real preparation/publication/commit; the controller chooses when to fail.
try
{
    var input = JsonSerializer.Deserialize<WorkerInput>((await Console.In.ReadLineAsync())!)!;
    var probe = new PhaseProbe();
    var services = new ServiceCollection();
    services.AddLogging(logging => logging.ClearProviders());
    services.AddNgbRuntime();
    services.AddNgbPostgres(input.ConnectionString);
    services.AddScoped<IUnitOfWork>(_ => new ProbedUnitOfWork(
        new PostgresUnitOfWork(input.ConnectionString, NullLogger<PostgresUnitOfWork>.Instance), probe));
    services.AddScoped<IOperationalRegisterDefaultProjectionRebuilder>(sp => new ProbedRebuilder(
        ActivatorUtilities.CreateInstance<PostgresOperationalRegisterDefaultProjectionRebuilder>(sp),
        sp.GetRequiredService<IUnitOfWork>(), probe));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var count = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>()
        .FinalizeRegisterDirtyAsync(input.RegisterId);
    Console.WriteLine($"RESULT|{count}");
    return 0;
}
catch (Exception error)
{
    // No SQL, connection strings or driver messages in process output.
    Console.Error.WriteLine($"FAULT|{error.GetType().Name}");
    return 23;
}

internal sealed record WorkerInput(string ConnectionString, Guid RegisterId);

internal sealed class PhaseProbe
{
    public async Task ReachAsync(string phase, IUnitOfWork uow)
    {
        Console.WriteLine($"PHASE|{phase}|{((NpgsqlConnection)uow.Connection).ProcessID}");
        await Console.Out.FlushAsync();
        if (await Console.In.ReadLineAsync() != "continue") throw new IOException("Test controller disconnected.");
    }
}

internal sealed class ProbedRebuilder(PostgresOperationalRegisterDefaultProjectionRebuilder inner, IUnitOfWork uow, PhaseProbe probe)
    : IOperationalRegisterDefaultProjectionRebuilder, IOperationalRegisterProjectionPreparation
{
    public Task RebuildMonthAsync(Guid id, DateOnly month, DateOnly? previous, CancellationToken ct = default)
        => inner.RebuildMonthAsync(id, month, previous, ct);
    public async Task<IOperationalRegisterPreparedProjection> PrepareMonthAsync(Guid id, DateOnly month, CancellationToken ct = default)
    {
        await probe.ReachAsync("preparing", uow);
        var prepared = await inner.PrepareMonthAsync(id, month, ct);
        await probe.ReachAsync("prepared", uow);
        return new ProbedProjection(prepared, uow, probe);
    }
    private sealed class ProbedProjection(IOperationalRegisterPreparedProjection inner, IUnitOfWork uow, PhaseProbe probe)
        : IOperationalRegisterPreparedProjection
    {
        public async Task CompleteAsync(CancellationToken ct = default)
        {
            await inner.CompleteAsync(ct);
            await probe.ReachAsync("caught-up", uow);
        }
    }
}

internal sealed class ProbedUnitOfWork(PostgresUnitOfWork inner, PhaseProbe probe) : IUnitOfWork
{
    public DbConnection Connection => inner.Connection;
    public DbTransaction? Transaction => inner.Transaction;
    public bool HasActiveTransaction => inner.HasActiveTransaction;
    public Task EnsureConnectionOpenAsync(CancellationToken ct = default) => inner.EnsureConnectionOpenAsync(ct);
    public Task BeginTransactionAsync(CancellationToken ct = default) => inner.BeginTransactionAsync(ct);
    public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
    public void EnsureActiveTransaction() => inner.EnsureActiveTransaction();
    public ValueTask DisposeAsync() => inner.DisposeAsync();
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await probe.ReachAsync("before-commit", this);
        await inner.CommitAsync(ct);
        await probe.ReachAsync("committed", this);
    }
}
