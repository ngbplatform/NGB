using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData("prepared", false)]
    [InlineData("caught-up", false)]
    [InlineData("before-commit", false)]
    [InlineData("committed", true)]
    public async Task Killed_worker_recovers_atomically_at_each_transaction_boundary(string phase, bool committed)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        await using var worker = await FinalizationFaultProcess.StartAsync(Fixture.ConnectionString, id);
        await ReachWorkerPhaseWithTailAsync(worker, host, id, phase);
        await AssertPublishedPairAsync(host, id, committed ? 150m : 100m, committed);
        await worker.KillAsync();
        await AssertRecoveryAsync(host, id, committed);
    }

    [Theory]
    [InlineData("prepared", false)]
    [InlineData("caught-up", false)]
    [InlineData("before-commit", false)]
    [InlineData("committed", true)]
    public async Task Terminated_database_session_recovers_without_partial_publication(string phase, bool committed)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        await using var worker = await FinalizationFaultProcess.StartAsync(Fixture.ConnectionString, id);
        var backend = await ReachWorkerPhaseWithTailAsync(worker, host, id, phase);
        await using (var fault = host.Services.CreateAsyncScope())
        {
            var uow = fault.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync();
            // Fault injection against this test's child connection in its isolated Testcontainer.
            (await uow.Connection.ExecuteScalarAsync<bool>("SELECT pg_terminate_backend(@Backend, 5000)", new { Backend = backend }))
                .Should().BeTrue();
        }
        await worker.ContinueAsync();
        (await worker.ExitAsync()).Should().Be(committed ? 0 : 23);
        await AssertRecoveryAsync(host, id, committed);
    }

    [Fact]
    public async Task Lost_successful_commit_response_does_not_duplicate_or_lose_projection_on_retry()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        await using var proxy = new LostCommitResponseProxy(Fixture.ConnectionString);
        await using var worker = await FinalizationFaultProcess.StartAsync(proxy.ConnectionString, id);
        await ReachWorkerPhaseWithTailAsync(worker, host, id, "before-commit");
        await AssertPublishedPairAsync(host, id, 100m, finalized: false);
        await worker.ContinueAsync();
        await proxy.CommitResponseDropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await worker.ExitAsync()).Should().Be(23, "the client did not receive PostgreSQL's commit acknowledgement");
        // Despite the client's failure, the database committed both projections and the marker.
        await AssertRecoveryAsync(host, id, committed: true);
    }

    private static async Task<int> ReachWorkerPhaseWithTailAsync(FinalizationFaultProcess worker,
        Microsoft.Extensions.Hosting.IHost host, Guid id, string target)
    {
        await worker.WaitForPhaseAsync("preparing");
        await worker.ContinueAsync();
        var backend = await worker.WaitForPhaseAsync("prepared");
        var tailDocument = Guid.CreateVersion7();
        await SeedDocumentAsync(host, tailDocument, JanuaryDate);
        await ApplyPostAsync(host, id, tailDocument, CreateMovement(tailDocument, JanuaryDate, 30m));
        if (target == "prepared") return backend;
        foreach (var phase in new[] { "caught-up", "before-commit", "committed" })
        {
            await worker.ContinueAsync();
            backend = await worker.WaitForPhaseAsync(phase);
            if (phase == target) return backend;
        }
        throw new ArgumentOutOfRangeException(nameof(target));
    }

    private static async Task AssertRecoveryAsync(Microsoft.Extensions.Hosting.IHost host, Guid id, bool committed)
    {
        await AssertPublishedPairAsync(host, id, committed ? 150m : 100m, committed);
        var connectionString = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NGB.PostgreSql.DependencyInjection.PostgresOptions>>().Value.ConnectionString;
        // Recovery uses a fresh OS process and DI container, not surviving in-process state.
        await using (var restarted = await FinalizationFaultProcess.StartAsync(connectionString, id))
        {
            if (!committed)
                foreach (var phase in new[] { "preparing", "prepared", "caught-up", "before-commit", "committed" })
                {
                    await restarted.WaitForPhaseAsync(phase);
                    await restarted.ContinueAsync();
                }
            (await restarted.ReadResultAsync()).Should().Be(committed ? 0 : 1);
            (await restarted.ExitAsync()).Should().Be(0);
        }
        await AssertPublishedPairAsync(host, id, 150m, finalized: true);
        var versions = await ProjectionVersionsAsync(host, id);
        (await FinalizeDirtyAsync(host, id)).Should().Be(0);
        await MarkMonthDirtyAsync(host, id, January);
        (await FinalizeDirtyAsync(host, id)).Should().Be(1);
        await AssertPublishedPairAsync(host, id, 150m, finalized: true);
        (await ProjectionVersionsAsync(host, id)).Should().Equal(versions, "a retry must not rewrite unchanged projections");
        // A new real writer and another finalization prove ORR/ORP/ORF were all released.
        var next = Guid.CreateVersion7();
        await SeedDocumentAsync(host, next, JanuaryDate);
        await ApplyPostAsync(host, id, next, CreateMovement(next, JanuaryDate, 7m)).WaitAsync(TimeSpan.FromSeconds(10));
        (await FinalizeDirtyAsync(host, id).WaitAsync(TimeSpan.FromSeconds(15))).Should().Be(1);
        await AssertPublishedPairAsync(host, id, 157m, finalized: true);
    }

    private static async Task AssertPublishedPairAsync(Microsoft.Extensions.Hosting.IHost host, Guid id, decimal value, bool finalized)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var balance = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, January);
        var turnover = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(id, January);
        balance.Single().Values["amount"].Should().Be(value);
        turnover.Single().Values["amount"].Should().Be(value);
        var marker = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>().GetAsync(id, January);
        marker!.Status.Should().Be(finalized ? OperationalRegisterFinalizationStatus.Finalized : OperationalRegisterFinalizationStatus.Dirty);
    }
}
