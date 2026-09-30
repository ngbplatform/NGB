using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.PropertyManagement.Migrator.Seed;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Seed;

public sealed class PropertyManagementSeedDemoCliFullCoverageTests
{
    [Fact]
    public void Command_detection_and_trimming_cover_empty_wrong_case_insensitive_and_argument_boundaries()
    {
        PropertyManagementSeedDemoCli.IsSeedDemoCommand([]).Should().BeFalse();
        PropertyManagementSeedDemoCli.IsSeedDemoCommand(["migrate"]).Should().BeFalse();
        PropertyManagementSeedDemoCli.IsSeedDemoCommand(["SeEd-DeMo"]).Should().BeTrue();

        PropertyManagementSeedDemoCli.TrimCommand([]).Should().BeEmpty();
        PropertyManagementSeedDemoCli.TrimCommand(["seed-demo"]).Should().BeEmpty();
        PropertyManagementSeedDemoCli.TrimCommand(["seed-demo", "--seed", "42"])
            .Should().Equal("--seed", "42");
    }

    [Fact]
    public void Options_parse_defaults_trims_dataset_and_accepts_all_inclusive_boundaries()
    {
        var defaults = PropertyManagementDemoSeedOptions.Parse(BaseArgs());
        defaults.ConnectionString.Should().Be("Host=localhost;Database=coverage");
        defaults.DatasetCode.Should().Be("demo");
        defaults.Buildings.Should().Be(6);
        defaults.SkipIfDatasetExists.Should().BeFalse();

        var minimum = PropertyManagementDemoSeedOptions.Parse(Args(
            "--dataset", "  boundary  ",
            "--from", "2026-08-22",
            "--to", "2026-08-22",
            "--buildings", "1",
            "--units-min", "1",
            "--units-max", "1",
            "--tenants", "1",
            "--vendors", "1",
            "--occupancy-rate", "0.000001",
            "--progress-every", "0",
            "--advisory-lock-timeout-seconds", "1",
            "--skip-if-dataset-exists", "true"));
        minimum.DatasetCode.Should().Be("boundary");
        minimum.SkipIfDatasetExists.Should().BeTrue();

        var maximum = PropertyManagementDemoSeedOptions.Parse(Args(
            "--buildings", "100",
            "--units-min", "1000",
            "--units-max", "1000",
            "--tenants", "50000",
            "--vendors", "5000",
            "--occupancy-rate", "1",
            "--advisory-lock-timeout-seconds", "3600"));
        maximum.Buildings.Should().Be(100);
        maximum.UnitsPerBuildingMin.Should().Be(1000);
        maximum.UnitsPerBuildingMax.Should().Be(1000);
        maximum.Tenants.Should().Be(50_000);
        maximum.Vendors.Should().Be(5_000);
        maximum.OccupancyRate.Should().Be(1d);
        maximum.AdvisoryLockWaitTimeoutSeconds.Should().Be(3600);
    }

    [Fact]
    public void Options_parse_rejects_reversed_dates_and_every_numeric_value_outside_its_domain()
    {
        var invalidCases = new (string[] Args, Type ExceptionType)[]
        {
            (Args("--from", "2026-08-23", "--to", "2026-08-22"), typeof(NgbArgumentInvalidException)),
            (Args("--buildings", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--buildings", "101"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--units-min", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--units-min", "1001", "--units-max", "1001"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--units-min", "2", "--units-max", "1"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--units-max", "1001"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--tenants", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--tenants", "50001"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--vendors", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--vendors", "5001"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--occupancy-rate", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--occupancy-rate", "1.000001"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--occupancy-rate", "NaN"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--occupancy-rate", "Infinity"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--progress-every", "-1"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--advisory-lock-timeout-seconds", "0"), typeof(NgbArgumentOutOfRangeException)),
            (Args("--advisory-lock-timeout-seconds", "3601"), typeof(NgbArgumentOutOfRangeException))
        };

        foreach (var invalidCase in invalidCases)
        {
            var act = () => PropertyManagementDemoSeedOptions.Parse(invalidCase.Args);
            act.Should().Throw<Exception>().Which.Should().BeOfType(invalidCase.ExceptionType);
        }
    }

    [Fact]
    public async Task Run_returns_failure_for_invalid_options_before_service_provider_creation()
    {
        var exitCode = await PropertyManagementSeedDemoCli.RunAsync(Args("--buildings", "0"));
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Scoped_execution_returns_result_and_always_disposes_scope()
    {
        var services = Mock.Of<IServiceProvider>();
        var scope = new Mock<IServiceScope>(MockBehavior.Strict);
        scope.SetupGet(x => x.ServiceProvider).Returns(services);
        scope.Setup(x => x.Dispose());
        var factory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        factory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var result = await PropertyManagementDemoSeeder.ExecuteInNewScopeAsync(
            factory.Object,
            (provider, ct) => Task.FromResult(provider == services && !ct.IsCancellationRequested ? 42 : -1),
            CancellationToken.None);
        Func<Task> failed = () => PropertyManagementDemoSeeder.ExecuteInNewScopeAsync<int>(
            factory.Object,
            (_, _) => throw new InvalidOperationException("scoped action failed"),
            CancellationToken.None);

        result.Should().Be(42);
        await failed.Should().ThrowAsync<InvalidOperationException>().WithMessage("scoped action failed");
        scope.Verify(x => x.Dispose(), Times.Exactly(2));
    }

    private static string[] BaseArgs() => ["--connection", "Host=localhost;Database=coverage"];

    private static string[] Args(params string[] additional) => [.. BaseArgs(), .. additional];
}
