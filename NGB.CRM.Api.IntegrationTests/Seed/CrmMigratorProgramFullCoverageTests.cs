using FluentAssertions;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Seed;

public sealed class CrmMigratorProgramFullCoverageTests
{
    [Fact]
    public async Task Main_DispatchesDefaultsDemoAndPlatformCommands()
    {
        (await NGB.CRM.Migrator.Program.Main(["seed-defaults", "--connection="]))
            .Should().Be(1);
        (await NGB.CRM.Migrator.Program.Main(["seed-demo", "--connection="]))
            .Should().Be(1);
        (await NGB.CRM.Migrator.Program.Main(["--list-modules"]))
            .Should().Be(0);
    }
}
