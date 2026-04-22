using System.Collections.Concurrent;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterProjectionRunner_RollbackSafety_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Runner_WhenProjectorThrows_ManageTransactionTrue_RollsBackProjections_AndKeepsDirty()
    {
        var state = new ProjectorState();
        var callLog = new ProjectorCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, ThrowingAfterWriteProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("v", "V", 1)
        });

        state.NonEmptySetId = await CreateNonEmptyDimensionSetIdAsync(host);

        var monthAny = new DateOnly(2026, 1, 15); // not month-start on purpose
        await SeedOldProjectionsAsync(host, registerId, monthAny, state.NonEmptySetId);

        // Mark dirty via movements write
        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);
        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal))
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var r = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            r.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Finalize (projector throws after writing) -> everything must roll back
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var act = async () => await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*BOOM*");
        }

        callLog.Calls.Should().HaveCount(1);

        // Finalization marker must remain Dirty
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            row.FinalizedAtUtc.Should().BeNull();
            row.DirtySinceUtc.Should().NotBeNull();
        }

        // Projections must remain unchanged (old payload)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var t = await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None);

            ExtractInt(t, Guid.Empty, "v").Should().Be(1);
            ExtractInt(t, state.NonEmptySetId, "v").Should().Be(2);

            ExtractInt(b, Guid.Empty, "v").Should().Be(1);
            ExtractInt(b, state.NonEmptySetId, "v").Should().Be(2);
        }
    }

    [Fact]
    public async Task Runner_WhenCancelledAfterProjectionsWrite_ManageTransactionTrue_RollsBackProjections_AndKeepsDirty()
    {
        var state = new ProjectorState();
        var callLog = new ProjectorCallLog();
        var trigger = new CancelAfterWriteTrigger();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(callLog);
                services.AddSingleton(trigger);
                services.AddScoped<IOperationalRegisterMonthProjector, CancellableAfterWriteProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("v", "V", 1)
        });

        state.NonEmptySetId = await CreateNonEmptyDimensionSetIdAsync(host);

        var monthAny = new DateOnly(2026, 1, 15); // not month-start on purpose
        await SeedOldProjectionsAsync(host, registerId, monthAny, state.NonEmptySetId);

        // Mark dirty via movements write
        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);
        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal))
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var r = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            r.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Finalize (projector cancels after writing) -> everything must roll back
        trigger.Arm();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var act = async () => await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        callLog.Calls.Should().HaveCount(1);

        // Finalization marker must remain Dirty
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            row.FinalizedAtUtc.Should().BeNull();
            row.DirtySinceUtc.Should().NotBeNull();
        }

        // Projections must remain unchanged (old payload)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var t = await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None);

            ExtractInt(t, Guid.Empty, "v").Should().Be(1);
            ExtractInt(t, state.NonEmptySetId, "v").Should().Be(2);

            ExtractInt(b, Guid.Empty, "v").Should().Be(1);
            ExtractInt(b, state.NonEmptySetId, "v").Should().Be(2);
        }
    }

    [Fact]
    public async Task Runner_WhenProjectorThrows_ManageTransactionFalse_RespectsOuterRollback()
    {
        var state = new ProjectorState();
        var callLog = new ProjectorCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, ThrowingAfterWriteProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("v", "V", 1)
        });

        state.NonEmptySetId = await CreateNonEmptyDimensionSetIdAsync(host);

        var monthAny = new DateOnly(2026, 2, 20);
        await SeedOldProjectionsAsync(host, registerId, monthAny, state.NonEmptySetId);

        // Dirty via movements
        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);
        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal))
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var r = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            r.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Run inside external transaction; runner shouldn't auto-rollback.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var act = async () => await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*BOOM*");

            await uow.RollbackAsync(CancellationToken.None);
        }

        callLog.Calls.Should().HaveCount(1);

        // Finalization marker must remain Dirty
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            row.FinalizedAtUtc.Should().BeNull();
            row.DirtySinceUtc.Should().NotBeNull();
        }

        // Projections must remain unchanged (old payload)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var t = await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 2, 1), ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, new DateOnly(2026, 2, 1), ct: CancellationToken.None);

            ExtractInt(t, Guid.Empty, "v").Should().Be(1);
            ExtractInt(t, state.NonEmptySetId, "v").Should().Be(2);

            ExtractInt(b, Guid.Empty, "v").Should().Be(1);
            ExtractInt(b, state.NonEmptySetId, "v").Should().Be(2);
        }
    }

    private static int ExtractInt(
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        Guid dimensionSetId,
        string propertyName)
    {
        var row = rows.Single(r => r.DimensionSetId == dimensionSetId);
        row.Values.TryGetValue(propertyName, out var v).Should().BeTrue();
        return (int)v;
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task SeedDocumentAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await docs.CreateAsync(
            new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = "IT-1",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            },
            CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateNonEmptyDimensionSetIdAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        var code = "it_dim_" + dimensionId.ToString("N")[..8];
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dimensionId, Code = code, Name = "Integration Test Dimension" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimensionId, valueId)
        });

        await uow.CommitAsync(CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);
        var id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        return id;
    }

    private static async Task SeedOldProjectionsAsync(
        IHost host,
        Guid registerId,
        DateOnly anyDateInMonth,
        Guid nonEmptySetId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        var rows = new[]
        {
            new OperationalRegisterMonthlyProjectionRow(Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 1m }),
            new OperationalRegisterMonthlyProjectionRow(nonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 2m })
        };

        await uow.BeginTransactionAsync(CancellationToken.None);

        await turnovers.EnsureSchemaAsync(registerId, CancellationToken.None);
        await balances.EnsureSchemaAsync(registerId, CancellationToken.None);

        await turnovers.ReplaceForMonthAsync(registerId, anyDateInMonth, rows, CancellationToken.None);
        await balances.ReplaceForMonthAsync(registerId, anyDateInMonth, rows, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }

    private sealed class ProjectorState
    {
        public Guid NonEmptySetId { get; set; }
    }

    private sealed class ProjectorCallLog
    {
        private readonly ConcurrentQueue<(Guid RegisterId, DateOnly PeriodMonth)> _calls = new();

        public IReadOnlyList<(Guid RegisterId, DateOnly PeriodMonth)> Calls => _calls.ToArray();

        public void Add(Guid registerId, DateOnly periodMonth) => _calls.Enqueue((registerId, periodMonth));
    }

    private sealed class ThrowingAfterWriteProjector(
        ProjectorState state,
        ProjectorCallLog log,
        IOperationalRegisterTurnoversStore turnovers,
        IOperationalRegisterBalancesStore balances)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => "rr";

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            // Runner must execute projector inside the same transaction.
            context.UnitOfWork.EnsureActiveTransaction();

            if (state.NonEmptySetId == Guid.Empty)
                throw new XunitException("Test misconfiguration: NonEmptySetId is empty.");

            log.Add(context.RegisterId, context.PeriodMonth);

            await turnovers.EnsureSchemaAsync(context.RegisterId, ct);
            await balances.EnsureSchemaAsync(context.RegisterId, ct);

            var rows = new[]
            {
                new OperationalRegisterMonthlyProjectionRow(Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 999m }),
                new OperationalRegisterMonthlyProjectionRow(state.NonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 888m })
            };

            await turnovers.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, ct);
            await balances.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, ct);

            throw new NotSupportedException("BOOM: projector failed after writing projections.");
        }
    }

    private sealed class CancelAfterWriteTrigger
    {
        private readonly CancellationTokenSource _cts = new();
        private int _armed;

        public CancellationToken Token => _cts.Token;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public void CancelIfArmed()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
                _cts.Cancel();
        }
    }

    private sealed class CancellableAfterWriteProjector(
        ProjectorState state,
        ProjectorCallLog log,
        CancelAfterWriteTrigger trigger,
        IOperationalRegisterTurnoversStore turnovers,
        IOperationalRegisterBalancesStore balances)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => "rr";

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            // Runner must execute projector inside the same transaction.
            context.UnitOfWork.EnsureActiveTransaction();

            if (state.NonEmptySetId == Guid.Empty)
                throw new XunitException("Test misconfiguration: NonEmptySetId is empty.");

            log.Add(context.RegisterId, context.PeriodMonth);

            await turnovers.EnsureSchemaAsync(context.RegisterId, CancellationToken.None);
            await balances.EnsureSchemaAsync(context.RegisterId, CancellationToken.None);

            var rows = new[]
            {
                new OperationalRegisterMonthlyProjectionRow(Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 999m }),
                new OperationalRegisterMonthlyProjectionRow(state.NonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["v"] = 888m })
            };

            // Ensure the SQL executes even if ct is cancelled later.
            await turnovers.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, CancellationToken.None);
            await balances.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, CancellationToken.None);

            trigger.CancelIfArmed();
            ct.ThrowIfCancellationRequested();
        }
    }
}
