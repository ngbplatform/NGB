using FluentAssertions;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

public sealed class TradeMigratorProgramFullCoverageTests
{
    [Fact]
    public async Task Main_DispatchesDefaultsDemoAndPlatformCommands()
    {
        (await NGB.Trade.Migrator.Program.Main(["seed-defaults", "--connection="]))
            .Should().Be(2);
        (await NGB.Trade.Migrator.Program.Main(["seed-demo", "--connection="]))
            .Should().Be(1);
        (await NGB.Trade.Migrator.Program.Main(["--list-modules"]))
            .Should().Be(0);
    }
}
