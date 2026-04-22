using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for PostgreSQL-backed integration tests.
/// Ensures DB is reset (Respawn) before each test method execution.
/// </summary>
public abstract class IntegrationTestBase(PostgresTestFixture fixture) : IAsyncLifetime
{
    protected PostgresTestFixture Fixture { get; } = fixture;

    public Task InitializeAsync() => Fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
