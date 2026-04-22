using System.Reflection;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Documents;
using NGB.PostgreSql.Migrations.OperationalRegisters;
using NGB.PostgreSql.Migrations.Platform;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: hard ordering contracts for Operational Registers migrations.
///
/// Why this matters:
/// - Operational register migrations introduce generated columns, FK targets and DB-guards.
/// - If ordering drifts, migrations can succeed partially (idempotent CREATE IF NOT EXISTS) but leave the DB broken.
///
/// This test is intentionally reflection-based (no DB needed) to keep it fast and deterministic.
/// </summary>
public sealed class DatabaseBootstrapper_OrderContracts_OperationalRegisters_P1Tests
{
    [Fact]
    public void Bootstrapper_OrderMustRespect_HardDependencies_ForOperationalRegisters()
    {
        var wired = BuildBootstrapperSet().Select(x => x.GetType()).ToList();

        int IndexOf(Type t)
        {
            var idx = wired.IndexOf(t);
            idx.Should().BeGreaterThanOrEqualTo(0, $"bootstrapper must include {t.Name}");
            return idx;
        }

        void RequireBefore(Type before, Type after, string because)
            => IndexOf(before).Should().BeLessThan(IndexOf(after), because);

        // FK dependency: dimension rules reference platform_dimensions.
        RequireBefore(typeof(PlatformDimensionsMigration), typeof(OperationalRegisterDimensionRulesMigration),
            "operational register dimension rules reference platform_dimensions via FK");

        // FK dependency: write log references documents.
        RequireBefore(typeof(DocumentsMigration), typeof(OperationalRegisterWriteStateMigration),
            "operational register write log references documents via FK");

        // Registry must exist before any dependent tables/columns/guards.
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegistersCodeNormMigration),
            "generated code_norm column is added to operational_registers");
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegistersTableCodeMigration),
            "generated table_code column is added to operational_registers");
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegisterResourcesMigration),
            "operational_register_resources references operational_registers via FK");
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegisterDimensionRulesMigration),
            "operational_register_dimension_rules references operational_registers via FK");
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegisterFinalizationsMigration),
            "operational_register_finalizations references operational_registers via FK");
        RequireBefore(typeof(OperationalRegistersMigration), typeof(OperationalRegisterWriteStateMigration),
            "operational_register_write_state references operational_registers via FK");
        RequireBefore(typeof(OperationalRegisterWriteStateMigration), typeof(OperationalRegisterWriteLogHistoryMigration),
            "immutable opreg write history depends on the mutable state contract and its FK targets");

        // Extra guards reference generated columns + rules table.
        RequireBefore(typeof(OperationalRegistersCodeNormMigration), typeof(OperationalRegisterExtraGuardsMigration),
            "extra guards may reference code_norm in comparisons");
        RequireBefore(typeof(OperationalRegistersTableCodeMigration), typeof(OperationalRegisterExtraGuardsMigration),
            "extra guards may reference table_code in comparisons");
        RequireBefore(typeof(OperationalRegisterDimensionRulesMigration), typeof(OperationalRegisterExtraGuardsMigration),
            "extra guards attach trigger to operational_register_dimension_rules");

        // Indexes rely on generated columns and dependent tables.
        RequireBefore(typeof(OperationalRegistersCodeNormMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_registers.code_norm");
        RequireBefore(typeof(OperationalRegistersTableCodeMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_registers.table_code");
        RequireBefore(typeof(OperationalRegisterResourcesMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_register_resources");
        RequireBefore(typeof(OperationalRegisterDimensionRulesMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_register_dimension_rules");
        RequireBefore(typeof(OperationalRegisterFinalizationsMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_register_finalizations");
        RequireBefore(typeof(OperationalRegisterWriteStateMigration), typeof(OperationalRegistersIndexesMigration),
            "indexes reference operational_register_write_state");
    }

    private static IDdlObject[] BuildBootstrapperSet()
    {
        var mi = typeof(DatabaseBootstrapper).GetMethod(
            "BuildPlatformDdlObjects",
            BindingFlags.Static | BindingFlags.NonPublic);

        mi.Should().NotBeNull();

        var result = mi!.Invoke(null, null);
        result.Should().BeOfType<IDdlObject[]>();

        return (IDdlObject[])result!;
    }
}
