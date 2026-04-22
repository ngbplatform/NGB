using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DefaultProjector_PrunesZeroBalanceSnapshots_AndContinuesChainFromEmptySnapshot()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var registerCode = "it_opreg_zero_" + Guid.CreateVersion7().ToString("N")[..8];
        var janDocId = Guid.CreateVersion7();
        var febDocId = Guid.CreateVersion7();
        var marDocId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId, registerCode, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await SeedDocumentAsync(host, janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, febDocId, new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        await ApplyPostAsync(host, registerId, janDocId, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 100m));
        await ApplyPostAsync(host, registerId, febDocId, CreateMovement(febDocId, new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc), -100m));
        await ApplyPostAsync(host, registerId, marDocId, CreateMovement(marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), 50m));

        var finalized = await FinalizeDirtyAsync(host, registerId);
        finalized.Should().Be(3);

        await using var scope = host.Services.CreateAsyncScope();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
            .Single().Values["amount"].Should().Be(100m);

        (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 2, 1), ct: CancellationToken.None))
            .Should().BeEmpty("a cumulative zero snapshot should not keep a synthetic all-zero row");

        (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
            .Single().Values["amount"].Should().Be(50m, "March must continue from the empty February snapshot as zero baseline");
    }

    [Fact]
    public async Task DefaultProjector_BuildsSparseCumulativeBalances_AndHistoricalRepostInvalidatesFutureFinalizedMonths()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var registerCode = "it_opreg_bal_" + Guid.CreateVersion7().ToString("N")[..8];
        var janDocId = Guid.CreateVersion7();
        var marDocId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId, registerCode, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await SeedDocumentAsync(host, janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        await ApplyPostAsync(
            host,
            registerId,
            janDocId,
            new OperationalRegisterMovement(
                janDocId,
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 100m
                }));

        await ApplyPostAsync(
            host,
            registerId,
            marDocId,
            new OperationalRegisterMovement(
                marDocId,
                new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 50m
                }));

        var finalizedInitial = await FinalizeDirtyAsync(host, registerId);
        finalizedInitial.Should().Be(2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            (await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(100m);

            (await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(50m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(100m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 2, 1), ct: CancellationToken.None))
                .Should().BeEmpty("months without finalized snapshots remain sparse");

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(150m, "March must carry forward the latest prior finalized balance");
        }

        await ApplyRepostAsync(
            host,
            registerId,
            janDocId,
            new OperationalRegisterMovement(
                janDocId,
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 80m
                }));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var fin = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            (await fin.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            (await fin.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty,
                    "historical January repost must invalidate future finalized snapshots");
        }

        var finalizedAfterRepost = await FinalizeDirtyAsync(host, registerId);
        finalizedAfterRepost.Should().Be(2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            (await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(80m);

            (await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(50m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(80m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(130m);
        }
    }

    [Fact]
    public async Task FinalizeDirtyAcrossAll_AfterHistoricalRepost_RebuildsChainInPeriodOrder_WhenDirtySinceMatches()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var registerCode = "it_opreg_all_" + Guid.CreateVersion7().ToString("N")[..8];
        var janDocId = Guid.CreateVersion7();
        var marDocId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId, registerCode, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await SeedDocumentAsync(host, janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        await ApplyPostAsync(host, registerId, janDocId, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 100m));
        await ApplyPostAsync(host, registerId, marDocId, CreateMovement(marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), 50m));

        var initialFinalized = await FinalizeDirtyAcrossAllAsync(host);
        initialFinalized.Should().Be(2);

        await ApplyRepostAsync(host, registerId, janDocId, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 80m));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            var mar = await finalizations.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None);

            jan.Should().NotBeNull();
            mar.Should().NotBeNull();
            jan!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            mar!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            jan.DirtySinceUtc.Should().Be(mar.DirtySinceUtc, "the repost invalidation marks the whole future chain in one operation");
        }

        var refinalized = await FinalizeDirtyAcrossAllAsync(host);
        refinalized.Should().Be(2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(80m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(130m,
                    "global dirty finalization must rebuild January before March when both months share the same dirty timestamp");
        }
    }

    [Fact]
    public async Task FinalizeDirtyAcrossAll_RespectsMaxItems_ForHistoricalInvalidatedChain_AndLeavesTailDirty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var registerCode = "it_opreg_cap_" + Guid.CreateVersion7().ToString("N")[..8];
        var janDocId = Guid.CreateVersion7();
        var marDocId = Guid.CreateVersion7();
        var mayDocId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId, registerCode, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await SeedDocumentAsync(host, janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(host, mayDocId, new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc));

        await ApplyPostAsync(host, registerId, janDocId, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 100m));
        await ApplyPostAsync(host, registerId, marDocId, CreateMovement(marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), 50m));
        await ApplyPostAsync(host, registerId, mayDocId, CreateMovement(mayDocId, new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc), 20m));

        (await FinalizeDirtyAcrossAllAsync(host)).Should().Be(3);

        await ApplyRepostAsync(host, registerId, janDocId, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 80m));

        var firstPass = await FinalizeDirtyAcrossAllAsync(host, maxItems: 2);
        firstPass.Should().Be(2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            (await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);

            (await finalizations.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);

            (await finalizations.GetAsync(registerId, new DateOnly(2026, 5, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty,
                    "bounded work must leave the tail of the invalidated chain dirty for the next pass");

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(80m);

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(130m);
        }

        var secondPass = await FinalizeDirtyAcrossAllAsync(host, maxItems: 2);
        secondPass.Should().Be(1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 5, 1), ct: CancellationToken.None))
                .Single().Values["amount"].Should().Be(150m);
        }
    }

    private static async Task<int> FinalizeDirtyAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
        return await runner.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 20, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task<int> FinalizeDirtyAcrossAllAsync(IHost host, int maxItems = 20)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
        return await runner.FinalizeDirtyAsync(maxItems: maxItems, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task ApplyPostAsync(
        IHost host,
        Guid registerId,
        Guid documentId,
        OperationalRegisterMovement movement)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        (await applier.ApplyMovementsForDocumentAsync(
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Post,
            [movement],
            affectedPeriods: null,
            manageTransaction: true,
            ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
    }

    private static async Task ApplyRepostAsync(
        IHost host,
        Guid registerId,
        Guid documentId,
        OperationalRegisterMovement movement)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        (await applier.ApplyMovementsForDocumentAsync(
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Repost,
            [movement],
            affectedPeriods: null,
            manageTransaction: true,
            ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
    }

    private static OperationalRegisterMovement CreateMovement(Guid documentId, DateTime occurredAtUtc, decimal amount)
        => new(
            documentId,
            occurredAtUtc,
            Guid.Empty,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["amount"] = amount
            });

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resourceRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, "IT Register"), nowUtc, CancellationToken.None);
        await resourceRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task SeedDocumentAsync(IHost host, Guid documentId, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await docs.CreateAsync(
            new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = "IT-" + documentId.ToString("N")[^12..],
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = dateUtc,
                UpdatedAtUtc = dateUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            },
            CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }
}
