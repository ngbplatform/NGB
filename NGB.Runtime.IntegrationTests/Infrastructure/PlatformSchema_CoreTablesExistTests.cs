using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class PlatformSchema_CoreTablesExistTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CoreTablesExist_AfterPlatformMigrations()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // IDbSchemaInspector is registered as Scoped (by design), so always resolve it from a scope.
        await using var scope = host.Services.CreateAsyncScope();
        var inspector = scope.ServiceProvider.GetRequiredService<IDbSchemaInspector>();

        var snapshot = await inspector.GetSnapshotAsync(CancellationToken.None);

        snapshot.Tables.Should().Contain([
            "accounting_accounts",
            "accounting_balances",
            "accounting_closed_periods",
            "accounting_posting_state",
            "accounting_register_main",
            "accounting_turnovers"
        ]);

        snapshot.ColumnsByTable.Should().ContainKey("accounting_accounts");
        snapshot.ColumnsByTable.Should().ContainKey("accounting_register_main");
        snapshot.ColumnsByTable.Should().ContainKey("accounting_turnovers");
    }
}
