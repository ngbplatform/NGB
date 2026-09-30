using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Repeated_finalization_makes_progress_without_stopping_any_concurrent_writer(int writers)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (id, _) = await SeedProjectionAsync(host);
        var preparedBefore = gate.PrepareCount;
        await MarkMonthDirtyAsync(host, id, January);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var stop = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var committed = new int[writers];
        var tasks = Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await using var scope = host.Services.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                await uow.BeginTransactionAsync(deadline.Token);
                await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>()
                    .AppendAsync(id, [CreateMovement(Guid.CreateVersion7(), JanuaryDate, (writer + 1) / 16m)], deadline.Token);
                await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>()
                    .MarkDirtyAsync(id, January, DateTime.UtcNow, DateTime.UtcNow, deadline.Token);
                // Stop between transactions. Every successful commit is counted even when stopping.
                await uow.CommitAsync(CancellationToken.None);
                Interlocked.Increment(ref committed[writer]);
            }
        })).ToArray();
        try
        {
            var previous = new int[writers];
            for (var round = 0; round < 4; round++)
            {
                for (var writer = 0; writer < writers; writer++)
                    while (Volatile.Read(ref committed[writer]) <= previous[writer])
                    {
                        if (tasks[writer].IsCompleted) await tasks[writer];
                        await Task.Delay(1, deadline.Token);
                    }
                if (round == 0) gate.Release.TrySetResult();
                else running = FinalizeDirtyAsync(host, id);
                (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
                tasks.Should().OnlyContain(task => !task.IsCompleted,
                    "each publication must finish while all writers are still running");
                for (var writer = 0; writer < writers; writer++) previous[writer] = Volatile.Read(ref committed[writer]);
            }
            gate.PrepareCount.Should().Be(preparedBefore + 4, "new movements must not force a prefix recalculation");
        }
        finally
        {
            stop.Cancel();
            gate.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        await FinalizeDirtyAsync(host, id);
        var expected = 100m + Enumerable.Range(0, writers).Sum(writer => committed[writer] * (writer + 1) / 16m);
        await AssertPublishedPairAsync(host, id, expected, finalized: true);
        (await FinalizeDirtyAsync(host, id)).Should().Be(0);
    }

    [Theory]
    [InlineData(2025, 12)]
    [InlineData(2026, 2)]
    [InlineData(2028, 2)]
    [InlineData(2026, 4)]
    public async Task Prefix_and_tail_use_exact_UTC_month_boundaries_including_leap_day_and_year_rollover(int year, int month)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var id = Guid.CreateVersion7();
        await SeedRegisterAsync(host, id, "it_calendar_" + id.ToString("N"), [new("amount", "Amount", 1)]);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>().EnsureSchemaAsync(id);
            await uow.CommitAsync();
        }
        var period = new DateOnly(year, month, 1);
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        // PostgreSQL timestamps have microsecond precision: last representable instant before midnight.
        var dates = new[] { start.AddTicks(-10), start, end.AddTicks(-10), end };
        var prefix = dates.Select((date, index) => CreateMovement(Guid.CreateVersion7(), date, 1 << index)).ToArray();
        var tail = dates.Select((date, index) => CreateMovement(Guid.CreateVersion7(), date, 16 << index)).ToArray();
        await AppendBatchAsync(host, id, prefix);
        await MarkMonthDirtyAsync(host, id, period);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await AppendBatchAsync(host, id, tail); }
        finally { gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        await AssertDocumentModelAsync(host, id, [period], [.. prefix, .. tail]);
        await using var check = host.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(id, period))
            .Single().Values["amount"].Should().Be(102m);
        (await check.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, period))
            .Single().Values["amount"].Should().Be(119m);
    }
}
