using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizationRunner_Race_MarkDirty_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FinalizeRegisterDirty_WhileMarkDirtyRuns_DoesNotDeadlock_AndDirtyWins_AfterCommit()
    {
        var code = UniqueRegisterCode();
        var codeNorm = code.Trim().ToLowerInvariant();

        var callLog = new TestCallLog();
        var gate = new Gate();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddSingleton(gate);
                services.AddScoped<IOperationalRegisterMonthProjector>(_ =>
                    new BlockingMonthProjector(codeNorm, callLog, gate));
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code, name: "IT Register");

        var anyDateInMonth = new DateOnly(2026, 1, 15);
        var monthStart = new DateOnly(2026, 1, 1);

        // Start with a dirty month.
        await MarkDirtyAsync(host, registerId, anyDateInMonth);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act 1: start finalization and block inside the projector.
        var finalizeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            return await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: cts.Token);
        }, cts.Token);

        // Wait until projector is running (transaction holds the month lock).
        gate.ProjectorStarted.Wait(cts.Token);

        // Act 2: concurrently mark the same month dirty again.
        // This call must NOT deadlock. It should block on the same month lock until the finalizer commits,
        // then set status back to Dirty (dirty wins after finalizer commit).
        var markDirtyTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

            await svc.MarkDirtyAsync(registerId, anyDateInMonth, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        // Allow finalization to proceed.
        gate.AllowProjectorToFinish.TrySetResult();

        var finalizedCount = await finalizeTask;
        await markDirtyTask;

        finalizedCount.Should().Be(1, "the month was dirty and should have been finalized once");

        // After the concurrent MarkDirty, the month must end as Dirty again.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, monthStart, cts.Token);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty,
                "a new dirty mark after finalization commit must invalidate projections again");
            row.DirtySinceUtc.Should().NotBeNull();
            row.FinalizedAtUtc.Should().BeNull();
        }

        callLog.Calls.Should().HaveCount(1, "the projector must be invoked once during the first finalization");
        callLog.Calls.Single().RegisterId.Should().Be(registerId);
        callLog.Calls.Single().PeriodMonth.Should().Be(monthStart);

        // Sanity: runner can finalize again after the new dirty mark.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
            var again = await runner.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 50, manageTransaction: true, ct: cts.Token);
            again.Should().Be(1, "the month remained dirty after the concurrent mark and must be finalizable again");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, monthStart, cts.Token);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            row.FinalizedAtUtc.Should().NotBeNull();
            row.DirtySinceUtc.Should().BeNull();
        }

        callLog.Calls.Should().HaveCount(2, "the projector runs again when the month is finalized the second time");
    }

    private static string UniqueRegisterCode()
        => "RR_RACE_" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();

    private static async Task SeedRegisterAsync(IHost host, Guid registerId, string code, string name)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task MarkDirtyAsync(IHost host, Guid registerId, DateOnly anyDateInMonth)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

        await svc.MarkDirtyAsync(registerId, anyDateInMonth, manageTransaction: true, ct: CancellationToken.None);
    }

    private sealed class Gate
    {
        public ManualResetEventSlim ProjectorStarted { get; } = new(false);

        public TaskCompletionSource AllowProjectorToFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestCallLog
    {
        private readonly ConcurrentQueue<(Guid RegisterId, DateOnly PeriodMonth)> _calls = new();

        public IReadOnlyList<(Guid RegisterId, DateOnly PeriodMonth)> Calls => _calls.ToArray();

        public void Add(Guid registerId, DateOnly periodMonth)
            => _calls.Enqueue((registerId, periodMonth));
    }

    private sealed class BlockingMonthProjector(string registerCodeNorm, TestCallLog log, Gate gate)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => registerCodeNorm;

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            log.Add(context.RegisterId, context.PeriodMonth);
            gate.ProjectorStarted.Set();

            // Block until the test releases us.
            await gate.AllowProjectorToFinish.Task.WaitAsync(ct);
        }
    }
}
