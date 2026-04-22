using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Migrations;

/// <summary>
/// P0: Migration pack discovery/selection is deterministic and validated fail-fast,
/// before touching the database.
/// </summary>
public sealed class NgbSchemaMigrator_PackDiscovery_P0Tests
{
    [Fact]
    public void DiscoverPacks_DuplicatePackIds_ThrowsInvariantViolation()
    {
        Action act = () =>
        {
            _ = SchemaMigrator.DiscoverPacks(new[] { typeof(DuplicateContributorA).Assembly });
        };

        act.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("Duplicate migration pack ids:*");
    }

    [Fact]
    public void Plan_UnknownPackId_ThrowsArgumentInvalid_BeforeDb()
    {
        var discovered = new[]
        {
            new MigrationPack(
                Id: "platform",
                MigrationAssemblies: new[] { typeof(NgbSchemaMigrator_PackDiscovery_P0Tests).Assembly },
                DependsOn: Array.Empty<string>(),
                RepairAsync: null)
        };

        Action act = () =>
        {
            _ = SchemaMigrator.Plan(discovered, includePackIds: new[] { "unknown" });
        };

        var ex = act.Should().Throw<NgbArgumentInvalidException>().Which;
        ex.ParamName.Should().Be("includePackIds");
        ex.Reason.Should().Contain("Unknown migration pack id");
    }

    private sealed class DuplicateContributorA : IMigrationPackContributor
    {
        public IEnumerable<MigrationPack> GetPacks()
        {
            yield return new MigrationPack(
                Id: "dup",
                MigrationAssemblies: new[] { typeof(DuplicateContributorA).Assembly },
                DependsOn: Array.Empty<string>(),
                RepairAsync: null);
        }
    }

    private sealed class DuplicateContributorB : IMigrationPackContributor
    {
        public IEnumerable<MigrationPack> GetPacks()
        {
            yield return new MigrationPack(
                Id: "dup",
                MigrationAssemblies: new[] { typeof(DuplicateContributorB).Assembly },
                DependsOn: Array.Empty<string>(),
                RepairAsync: null);
        }
    }
}
