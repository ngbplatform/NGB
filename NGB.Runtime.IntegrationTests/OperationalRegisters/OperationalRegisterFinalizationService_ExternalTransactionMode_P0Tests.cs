using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: OperationalRegisterFinalizationService must fully support external transaction mode:
/// - manageTransaction=false without an ambient transaction must fail fast with canonical message,
/// - manageTransaction=false with an ambient transaction must not commit/rollback implicitly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizationService_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkDirty_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

        Func<Task> act = () => svc.MarkDirtyAsync(
            registerId: Guid.CreateVersion7(),
            period: Jan,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task MarkFinalized_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

        Func<Task> act = () => svc.MarkFinalizedAsync(
            registerId: Guid.CreateVersion7(),
            period: Jan,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task MarkDirty_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_opreg_finext_" + registerId.ToString("N")[..8];

        await SeedRegisterAsync(host, registerId, code);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await svc.MarkDirtyAsync(registerId, Jan, manageTransaction: false, ct: CancellationToken.None);

                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, Jan, CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            row.DirtySinceUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task MarkFinalized_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_opreg_finext2_" + registerId.ToString("N")[..8];

        await SeedRegisterAsync(host, registerId, code);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await svc.MarkFinalizedAsync(registerId, Jan, manageTransaction: false, ct: CancellationToken.None);

                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, Jan, CancellationToken.None);
            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            row.FinalizedAtUtc.Should().NotBeNull();
        }
    }

    private static async Task SeedRegisterAsync(IHost host, Guid registerId, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await regRepo.UpsertAsync(
                new OperationalRegisterUpsert(registerId, code, "Integration Test Register"),
                NowUtc,
                ct);
        }, CancellationToken.None);
    }
}
