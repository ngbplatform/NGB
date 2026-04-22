using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterEnsureSchema_Cancellation_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenCancelledAfterEnsure_RollsBackDdl_AndTableRemainsMissing()
    {
        using var host = CreateHostWithCancellationAfterEnsure();

        var registerId = await CreateRegisterWithSingleFieldAsync(host, code: "RR_CANCEL_ENSURE", name: "Ensure Cancel", CancellationToken.None);

        // Drop physical table to force ensure to run.
        await DropRecordsTableAsync(Fixture.ConnectionString, registerId, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var before = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            before.Should().NotBeNull();
            before.Records.Exists.Should().BeFalse();
        }

        // Ensure + cancel after ensure SQL executed (but before commit).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var trigger = scope.ServiceProvider.GetRequiredService<CancelAfterEnsureTrigger>();

            trigger.Arm();

            var act = async () => await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, ct: trigger.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // Table should still be missing (DDL rolled back).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var after = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            after.Should().NotBeNull();
            after.Records.Exists.Should().BeFalse();
        }

        // Extra safety: verify table does not exist via to_regclass.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            var tableCode = await conn.ExecuteScalarAsync<string?>(
                "SELECT table_code FROM reference_registers WHERE register_id = @Id",
                new { Id = registerId });

            tableCode.Should().NotBeNull();

            var table = $"refreg_{tableCode}__records";
            var exists = await conn.ExecuteScalarAsync<string?>(
                "SELECT to_regclass(@T)::text",
                new { T = $"public.{table}" });

            exists.Should().BeNull();
        }
    }

    private IHost CreateHostWithCancellationAfterEnsure()
    {
        // Swap records store with a wrapper that cancels after the SQL ensure executed.
        // This provides deterministic coverage of the rollback path for DDL ensure.
        return IntegrationHostFactory.Create(
            connectionString: Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<CancelAfterEnsureTrigger>();

                services.AddScoped<PostgresReferenceRegisterRecordsStore>();
                services.AddScoped<IReferenceRegisterRecordsStore>(sp =>
                    new CancelAfterEnsureRecordsStore(
                        sp.GetRequiredService<PostgresReferenceRegisterRecordsStore>(),
                        sp.GetRequiredService<CancelAfterEnsureTrigger>()));
            });
    }

    private static async Task<Guid> CreateRegisterWithSingleFieldAsync(
        IHost host,
        string code,
        string name,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var id = await mgmt.UpsertAsync(
            code,
            name,
            periodicity: ReferenceRegisterPeriodicity.Day,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct);

        await mgmt.ReplaceFieldsAsync(
            id,
            fields:
            [
                new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)
            ],
            ct);

        return id;
    }

    private static async Task DropRecordsTableAsync(string connectionString, Guid registerId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT table_code FROM reference_registers WHERE register_id = @Id";
        var tableCode = await conn.ExecuteScalarAsync<string?>(sql, new { Id = registerId });
        tableCode.Should().NotBeNull("register must exist");

        var table = $"refreg_{tableCode}__records";
        await conn.ExecuteAsync(new CommandDefinition($"DROP TABLE IF EXISTS {table};", cancellationToken: ct));
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

    private sealed class CancelAfterEnsureRecordsStore(
        PostgresReferenceRegisterRecordsStore inner,
        CancelAfterEnsureTrigger trigger)
        : IReferenceRegisterRecordsStore
    {
        public async Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
        {
            await inner.EnsureSchemaAsync(registerId, CancellationToken.None);
            trigger.CancelIfArmed();
            ct.ThrowIfCancellationRequested();
        }

        public Task AppendAsync(Guid registerId, IReadOnlyList<ReferenceRegisterRecordWrite> records, CancellationToken ct = default)
            => inner.AppendAsync(registerId, records, ct);
    }
}
