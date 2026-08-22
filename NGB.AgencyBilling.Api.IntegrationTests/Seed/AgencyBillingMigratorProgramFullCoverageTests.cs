using FluentAssertions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Seed;

public sealed class AgencyBillingMigratorProgramFullCoverageTests
{
    [Fact]
    public async Task Main_DispatchesDefaultsDemoAndPlatformCommands()
    {
        (await NGB.AgencyBilling.Migrator.Program.Main(["seed-defaults", "--connection="]))
            .Should().Be(2);
        (await NGB.AgencyBilling.Migrator.Program.Main(["seed-demo", "--connection="]))
            .Should().Be(1);
        (await NGB.AgencyBilling.Migrator.Program.Main(["--list-modules"]))
            .Should().Be(0);
    }
}
