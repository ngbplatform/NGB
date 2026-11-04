extern alias Migrator;

using NGB.PostgreSql.Bootstrap;
using Testcontainers.PostgreSql;
using Xunit;
using MigratorProgram = Migrator::NgbApplication.Migrator.Program;

namespace NgbApplication.IntegrationTests;

[CollectionDefinition("Migrator environment", DisableParallelization = true)]
public sealed class MigratorEnvironmentCollection
{
}

[Collection("Migrator environment")]
public sealed class MigratorHostTests
{
    [Fact]
    public async Task Migrator_refuses_missing_storage_and_seeds_disposable_storage_idempotently()
    {
        var previous = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", null);
            Assert.NotEqual(0, await MigratorProgram.Main([]));
            Assert.Equal(0, await MigratorProgram.Main(["--list-modules"]));
            await Assert.ThrowsAsync<ArgumentNullException>(() => MigratorProgram.Main(null!));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MigratorProgram.Main(["seed-administrator"]));
            Assert.Equal("NGB_CONNECTION_STRING is required.", error.Message);

            await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await postgres.StartAsync();
            await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", postgres.GetConnectionString());

            Assert.Equal(0, await MigratorProgram.Main(["seed-administrator"]));
            Assert.Equal(0, await MigratorProgram.Main(["seed-administrator"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", previous);
        }
    }
}
