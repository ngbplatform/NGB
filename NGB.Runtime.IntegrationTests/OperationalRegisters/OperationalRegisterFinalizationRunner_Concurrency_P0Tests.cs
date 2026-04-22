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
public sealed class OperationalRegisterFinalizationRunner_Concurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FinalizeRegisterDirty_ConcurrentCalls_SameMonth_InvokesProjectorOnce_AndFinalizesOnce()
    {
        var code = UniqueRegisterCode();
        var codeNorm = code.Trim().ToLowerInvariant();

        var callLog = new TestCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector>(_ =>
                    new TestMonthProjector(codeNorm, callLog));
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code, name: "IT Register");

        var anyDateInMonth = new DateOnly(2026, 1, 15);
        await MarkDirtyAsync(host, registerId, anyDateInMonth);

        // Act: 12 concurrent finalizers.
        var tasks = Enumerable.Range(0, 12)
            .Select(async _ =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

                return await runner.FinalizeRegisterDirtyAsync(
                    registerId,
                    maxPeriods: 50,
                    manageTransaction: true,
                    ct: CancellationToken.None);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Sum().Should().Be(1,
            "exactly one month is dirty; under concurrency only one call must finalize it");

        callLog.Calls.Should().HaveCount(1, "projector must run exactly once for the dirty month");
        callLog.Calls.Single().RegisterId.Should().Be(registerId);
        callLog.Calls.Single().PeriodMonth.Should().Be(new DateOnly(2026, 1, 1));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            row.FinalizedAtUtc.Should().NotBeNull();
            row.DirtySinceUtc.Should().BeNull();
        }
    }

    private static string UniqueRegisterCode()
        => "RR_CONC_" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();

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

        await svc.MarkDirtyAsync(
            registerId,
            anyDateInMonth,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private sealed class TestCallLog
    {
        private readonly ConcurrentQueue<(Guid RegisterId, DateOnly PeriodMonth)> _calls = new();

        public IReadOnlyList<(Guid RegisterId, DateOnly PeriodMonth)> Calls => _calls.ToArray();

        public void Add(Guid registerId, DateOnly periodMonth)
            => _calls.Enqueue((registerId, periodMonth));
    }

    private sealed class TestMonthProjector(string registerCodeNorm, TestCallLog log) : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => registerCodeNorm;

        public Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            log.Add(context.RegisterId, context.PeriodMonth);
            return Task.CompletedTask;
        }
    }
}
