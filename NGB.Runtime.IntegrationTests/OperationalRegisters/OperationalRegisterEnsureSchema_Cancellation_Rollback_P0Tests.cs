using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterEnsureSchema_Cancellation_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenCancelledAfterMovementsEnsure_RollsBackDdl_AndSchemaRemainsMissing()
    {
        using var host = CreateHostWithCancellationAfterEnsure();
        var registerId = await CreateRegisterWithSingleResourceAsync(host, code: "OR_CANCEL_ENSURE", name: "Ensure Cancel", CancellationToken.None);

        // Verify missing before.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.Movements.Exists.Should().BeFalse();
        }

        // Ensure + cancel after movements ensure executed (but before commit).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
            var trigger = scope.ServiceProvider.GetRequiredService<CancelAfterEnsureTrigger>();

            trigger.Arm();

            var act = async () => await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Schema should remain missing (all DDL rolled back).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.Movements.Exists.Should().BeFalse();
            health.Turnovers.Exists.Should().BeFalse();
            health.Balances.Exists.Should().BeFalse();
        }
    }

    private IHost CreateHostWithCancellationAfterEnsure()
    {
        // Swap movements store with a wrapper that cancels after the SQL ensure executed.
        // This provides deterministic coverage of the rollback path for DDL ensure.
        return IntegrationHostFactory.Create(
            connectionString: Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<CancelAfterEnsureTrigger>();

                services.AddScoped<PostgresOperationalRegisterMovementsStore>();
                services.AddScoped<IOperationalRegisterMovementsStore>(sp =>
                    new CancelAfterEnsureMovementsStore(
                        sp.GetRequiredService<PostgresOperationalRegisterMovementsStore>(),
                        sp.GetRequiredService<CancelAfterEnsureTrigger>()));
            });
    }

    private static async Task<Guid> CreateRegisterWithSingleResourceAsync(
        IHost host,
        string code,
        string name,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        var id = await mgmt.UpsertAsync(code, name, ct);
        await mgmt.ReplaceResourcesAsync(
            id,
            resources:
            [
                new OperationalRegisterResourceDefinition("amount", "Amount", 10)
            ],
            ct: ct);

        return id;
    }

    private sealed class CancelAfterEnsureTrigger
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

    private sealed class CancelAfterEnsureMovementsStore(
        PostgresOperationalRegisterMovementsStore inner,
        CancelAfterEnsureTrigger trigger)
        : IOperationalRegisterMovementsStore
    {
        public async Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
        {
            await inner.EnsureSchemaAsync(registerId, CancellationToken.None);
            trigger.CancelIfArmed();
            ct.ThrowIfCancellationRequested();
        }

        public Task AppendAsync(
            Guid registerId,
            IReadOnlyList<OperationalRegisterMovement> movements,
            CancellationToken ct = default)
            => inner.AppendAsync(registerId, movements, ct);

        public Task AppendStornoByDocumentAsync(
            Guid registerId,
            Guid documentId,
            CancellationToken ct = default)
            => inner.AppendStornoByDocumentAsync(registerId, documentId, ct);
    }
}
