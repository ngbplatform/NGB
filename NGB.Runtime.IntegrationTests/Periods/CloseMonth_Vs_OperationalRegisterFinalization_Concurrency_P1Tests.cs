using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_Vs_OperationalRegisterFinalization_Concurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly ActorIdentity TestActor = new(
        AuthSubject: "it|close-month-opreg",
        Email: "it@ngb.local",
        DisplayName: "IT User");

    [Fact]
    public async Task CloseMonth_BlockedInAudit_DoesNotBlock_OperationalRegisterFinalization_RunDirtyMonths()
    {
        var auditGate = new AsyncGate();

        var regCode = UniqueRegisterCode();
        var regCodeNorm = regCode.Trim().ToLowerInvariant();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(auditGate);
                services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(TestActor));

                // Decorate the default PostgresAuditEventWriter to block CloseMonth at the very end of the transaction.
                services.RemoveAll<IAuditEventWriter>();
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new BlockingAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        sp.GetRequiredService<AsyncGate>(),
                        blockOnlyActionCode: AuditActionCodes.PeriodCloseMonth));

                // Provide a projector so finalization can proceed.
                services.AddScoped<IOperationalRegisterMonthProjector>(_ =>
                    new NoOpMonthProjector(regCodeNorm));
            });

        // Arrange: minimal accounting activity so CloseMonth actually persists balances.
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        // Arrange: a dirty operational register month to finalize.
        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, regCode, "IT Register");
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 1, 10));

        var period = new DateOnly(2026, 1, 1);

        // Act: start CloseMonth and wait until it is blocked in the audit writer.
        var closeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseMonthAsync(period, closedBy: "it", CancellationToken.None);
        });

        await auditGate.Entered.WaitAsync(TimeSpan.FromSeconds(15));

        // Act: while CloseMonth is still inside its transaction, finalize dirty operational register months.
        int finalized;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            // Mirror the background job path: opreg.finalization.run_dirty_months -> admin maintenance -> runner.
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
            finalized = await maintenance.FinalizeDirtyAsync(maxItems: 50, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15));
        }

        finalized.Should().Be(1);

        // Allow CloseMonth to complete.
        auditGate.Release();
        await closeTask.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: period is closed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closed = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Readers.Periods.IClosedPeriodReader>();
            (await closed.GetClosedAsync(period, period, CancellationToken.None)).Should().HaveCount(1);
        }

        // Assert: opreg month finalized.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var row = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
    }

    [Fact]
    public async Task OperationalRegisterFinalizationRunDirtyMonths_BlockedInProjector_DoesNotBlock_CloseMonth()
    {
        var projectorGate = new AsyncGate();

        var regCode = UniqueRegisterCode();
        var regCodeNorm = regCode.Trim().ToLowerInvariant();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(projectorGate);
                services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(TestActor));

                services.AddScoped<IOperationalRegisterMonthProjector>(_ =>
                    new BlockingMonthProjector(regCodeNorm, projectorGate));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, regCode, "IT Register");
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 1, 10));

        var period = new DateOnly(2026, 1, 1);

        // Act: start opreg finalization and wait until it is blocked inside projector.
        var finalizeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            // Mirror the background job path: opreg.finalization.run_dirty_months -> admin maintenance -> runner.
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
            return await maintenance.FinalizeDirtyAsync(maxItems: 50, CancellationToken.None);
        });

        await projectorGate.Entered.WaitAsync(TimeSpan.FromSeconds(15));

        // Act: while opreg finalization holds its transaction/period lock, CloseMonth should still complete.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseMonthAsync(period, closedBy: "it", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15));
        }

        // Allow finalization to complete.
        projectorGate.Release();
        (await finalizeTask.WaitAsync(TimeSpan.FromSeconds(15))).Should().Be(1);

        // Assert both states.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closed = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Readers.Periods.IClosedPeriodReader>();
            (await closed.GetClosedAsync(period, period, CancellationToken.None)).Should().HaveCount(1);

            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            (await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!.Status
                .Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
    }

    private static string UniqueRegisterCode()
        => "RR_CM_OPREG_" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();

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

        await svc.MarkDirtyAsync(registerId, anyDateInMonth, manageTransaction: true, CancellationToken.None);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void SignalEntered() => _entered.TrySetResult(true);

        public void Release() => _released.TrySetResult(true);

        public Task WaitReleaseAsync() => _released.Task;
    }

    private sealed class BlockingAuditEventWriter(
        IAuditEventWriter inner,
        AsyncGate gate,
        string blockOnlyActionCode)
        : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);

            if (auditEvent.ActionCode == blockOnlyActionCode)
            {
                gate.SignalEntered();
                await gate.WaitReleaseAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }

    private sealed class NoOpMonthProjector(string registerCodeNorm) : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => registerCodeNorm;

        public Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class BlockingMonthProjector(string registerCodeNorm, AsyncGate gate) : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => registerCodeNorm;

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            gate.SignalEntered();
            await gate.WaitReleaseAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
