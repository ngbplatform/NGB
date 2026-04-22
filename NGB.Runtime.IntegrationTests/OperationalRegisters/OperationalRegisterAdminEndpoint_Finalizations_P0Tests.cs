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

/// <summary>
/// P0: Admin endpoint exposes dirty/blocked finalizations and allows running the finalization runner.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterAdminEndpoint_Finalizations_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DirtyToFinalized_WithProjector_Works()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddScoped<IOperationalRegisterMonthProjector, RentRollNoOpProjector>());

        var registerId = await CreateRegisterWithSingleResourceAsync(host, code: "RR", name: "Rent Roll", CancellationToken.None);
        var period = new DateOnly(2026, 1, 1);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        await endpoint.MarkFinalizationDirtyAsync(registerId, period, CancellationToken.None);

        var dirty = await endpoint.GetDirtyFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        dirty.Should().HaveCount(1);
        dirty[0].RegisterId.Should().Be(registerId);
        dirty[0].Period.Should().Be(period);
        dirty[0].Status.Should().Be(nameof(OperationalRegisterFinalizationStatus.Dirty));

        var finalizedCount = await endpoint.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 10, CancellationToken.None);
        finalizedCount.Should().Be(1);

        var marker = await endpoint.GetFinalizationAsync(registerId, period, CancellationToken.None);
        marker.Should().NotBeNull();
        marker!.Status.Should().Be(nameof(OperationalRegisterFinalizationStatus.Finalized));

        var dirtyAfter = await endpoint.GetDirtyFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        dirtyAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task Blocked_ListAndUnblock_Works()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateRegisterWithSingleResourceAsync(host, code: "BB", name: "Blocked", CancellationToken.None);
        var period = new DateOnly(2026, 1, 1);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

        var nowUtc = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.MarkBlockedNoProjectorAsync(
            registerId,
            period,
            blockedSinceUtc: nowUtc,
            blockedReason: "no_projector",
            nowUtc: nowUtc,
            CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        var blocked = await endpoint.GetBlockedFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        blocked.Should().HaveCount(1);
        blocked[0].Status.Should().Be(nameof(OperationalRegisterFinalizationStatus.BlockedNoProjector));
        blocked[0].BlockedSinceUtc.Should().NotBeNull();
        blocked[0].BlockedReason.Should().Be("no_projector");

        var dirty = await endpoint.GetDirtyFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        dirty.Should().BeEmpty();

        // Admin remediation: mark dirty again clears blocked state.
        await endpoint.MarkFinalizationDirtyAsync(registerId, period, CancellationToken.None);

        var blockedAfter = await endpoint.GetBlockedFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        blockedAfter.Should().BeEmpty();

        var dirtyAfter = await endpoint.GetDirtyFinalizationsByIdAsync(registerId, limit: 100, CancellationToken.None);
        dirtyAfter.Should().HaveCount(1);
        dirtyAfter[0].Status.Should().Be(nameof(OperationalRegisterFinalizationStatus.Dirty));
    }

    private static async Task<Guid> CreateRegisterWithSingleResourceAsync(
        IHost host,
        string code,
        string name,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(code, name, ct);

        await mgmt.ReplaceResourcesAsync(
            registerId,
            new[] { new OperationalRegisterResourceDefinition("Amount", "Amount", Ordinal: 10) },
            ct);

        return registerId;
    }

    private sealed class RentRollNoOpProjector(
        IOperationalRegisterTurnoversStore turnovers,
        IOperationalRegisterBalancesStore balances)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => "rr";

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext ctx, CancellationToken ct = default)
        {
            // Minimal projector for tests: ensure physical projection tables exist and wipe rows for the month.
            await turnovers.EnsureSchemaAsync(ctx.RegisterId, ct);
            await balances.EnsureSchemaAsync(ctx.RegisterId, ct);

            await turnovers.ReplaceForMonthAsync(ctx.RegisterId, ctx.PeriodMonth, [], ct);
            await balances.ReplaceForMonthAsync(ctx.RegisterId, ctx.PeriodMonth, [], ct);
        }
    }
}
