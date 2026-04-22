using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizationService_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task MarkDirty_Then_MarkFinalized_Roundtrip_And_NormalizesToMonthStart()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

            // mid-month -> must normalize to month start
            await svc.MarkDirtyAsync(registerId, new DateOnly(2026, 1, 15), manageTransaction: true, ct: CancellationToken.None);
            await svc.MarkFinalizedAsync(registerId, new DateOnly(2026, 1, 20), manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var jan = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);

            jan.Should().NotBeNull();
            jan!.Period.Should().Be(new DateOnly(2026, 1, 1));
            jan.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            jan.FinalizedAtUtc.Should().NotBeNull();
            jan.DirtySinceUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task MarkDirty_ManageTransactionFalse_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

        var act = async () => await svc.MarkDirtyAsync(registerId, new DateOnly(2026, 1, 1), manageTransaction: false, ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage(TxnRequired);
    }

    [Fact]
    public async Task MarkDirty_ManageTransactionFalse_RespectsOuterRollback()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await svc.MarkDirtyAsync(registerId, new DateOnly(2026, 2, 10), manageTransaction: false, ct: CancellationToken.None);
            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var feb = await repo.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None);
            feb.Should().BeNull();
        }
    }

    [Fact]
    public async Task MarkDirty_WhenRegisterMissing_ThrowsClearError()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

        var act = async () => await svc.MarkDirtyAsync(Guid.CreateVersion7(), new DateOnly(2026, 1, 1), manageTransaction: true, ct: CancellationToken.None);

        await act.Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    [Fact]
    public async Task MarkDirty_CascadesToTrackedFuturePeriods_Only()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            await repo.MarkFinalizedAsync(registerId, new DateOnly(2026, 1, 1), nowUtc, nowUtc, CancellationToken.None);
            await repo.MarkFinalizedAsync(registerId, new DateOnly(2026, 3, 1), nowUtc, nowUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();
            await svc.MarkDirtyAsync(registerId, new DateOnly(2026, 1, 15), manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            (await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            (await repo.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None))
                .Should().BeNull("untracked months must not be materialized just for invalidation");

            (await repo.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
        }
    }

    private static async Task SeedRegisterAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }
}
