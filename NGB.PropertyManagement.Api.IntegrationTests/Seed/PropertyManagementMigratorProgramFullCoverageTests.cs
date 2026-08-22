using FluentAssertions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Seed;

public sealed class PropertyManagementMigratorProgramFullCoverageTests
{
    [Fact]
    public async Task Main_DispatchesDefaultsDemoAndPlatformCommands()
    {
        (await NGB.PropertyManagement.Migrator.Program.Main(["seed-defaults", "--connection="]))
            .Should().Be(2);
        (await NGB.PropertyManagement.Migrator.Program.Main(["seed-demo", "--connection="]))
            .Should().Be(1);
        (await NGB.PropertyManagement.Migrator.Program.Main(["--list-modules"]))
            .Should().Be(0);
    }
}
