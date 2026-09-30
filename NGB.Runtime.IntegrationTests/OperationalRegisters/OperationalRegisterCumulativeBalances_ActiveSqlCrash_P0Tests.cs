using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Crash_during_executing_projection_SQL_rolls_back_and_releases_all_locks(bool tail, bool terminateBackend)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        await using var blocker = host.Services.CreateAsyncScope();
        var db = blocker.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await db.EnsureConnectionOpenAsync();
        var register = await blocker.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(id);
        var table = OperationalRegisterNaming.BalancesTable(register!.TableCode);
        var token = Guid.NewGuid().ToString("N");
        var trigger = "fault_" + token;
        var function = "pause_" + token;
        var key = Random.Shared.Next(1, int.MaxValue);
        var sentinel = tail ? 150 : 120;
        await db.Connection.ExecuteAsync($"""
CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF NEW.amount = {sentinel} THEN PERFORM pg_advisory_xact_lock(1789341, {key}); END IF; RETURN NEW; END; $$;
CREATE TRIGGER {trigger} BEFORE UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION {function}();
""");
        await db.Connection.ExecuteAsync("SELECT pg_advisory_lock(1789341, @Key)", new { Key = key });
        var blockerPid = await db.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
        await using var worker = await FinalizationFaultProcess.StartAsync(Fixture.ConnectionString, id);
        try
        {
            var backend = await worker.WaitForPhaseAsync("preparing");
            await worker.ContinueAsync();
            if (tail)
            {
                await worker.WaitForPhaseAsync("prepared");
                await AppendFaultTailAsync(host, id);
                await worker.ContinueAsync();
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await db.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                       "SELECT @Blocker = ANY(pg_blocking_pids(@Backend))", new { Blocker = blockerPid, Backend = backend }, cancellationToken: deadline.Token)))
                await Task.Delay(5, deadline.Token);
            if (!tail)
            {
                // A writer really commits while the prefix aggregation is blocked in a SQL trigger.
                await AppendFaultTailAsync(host, id).WaitAsync(TimeSpan.FromSeconds(5));
            }
            await AssertPublishedPairAsync(host, id, 100m, finalized: false);
            if (terminateBackend)
            {
                (await db.Connection.ExecuteScalarAsync<bool>("SELECT pg_terminate_backend(@Backend, 5000)", new { Backend = backend })).Should().BeTrue();
                (await worker.ExitAsync()).Should().Be(23);
            }
            else await worker.KillAsync();
        }
        finally
        {
            await worker.KillAsync();
            await db.Connection.ExecuteAsync("SELECT pg_advisory_unlock(1789341, @Key)", new { Key = key });
            await db.Connection.ExecuteAsync($"DROP TRIGGER {trigger} ON {table}; DROP FUNCTION {function}();");
        }
        await AssertRecoveryAsync(host, id, committed: false);
    }

    private static async Task AppendFaultTailAsync(Microsoft.Extensions.Hosting.IHost host, Guid id)
    {
        var doc = Guid.CreateVersion7();
        await SeedDocumentAsync(host, doc, JanuaryDate);
        await ApplyPostAsync(host, id, doc, CreateMovement(doc, JanuaryDate, 30m));
    }
}
