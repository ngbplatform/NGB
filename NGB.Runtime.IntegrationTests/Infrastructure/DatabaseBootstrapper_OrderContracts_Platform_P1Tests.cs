using System.Reflection;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Documents;
using NGB.PostgreSql.Migrations.Platform;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Extra platform-level ordering contracts for DatabaseBootstrapper.
///
/// We keep these in a separate P1 test to avoid overloading the core P0 contract and to allow
/// adding new hard-dependency assertions without destabilizing earlier suites.
/// </summary>
public sealed class DatabaseBootstrapper_OrderContracts_Platform_P1Tests
{
    [Fact]
    public void Bootstrapper_PlatformOrder_MustRespect_HardDependencies()
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

        // Users
        RequireBefore(typeof(PlatformUsersMigration), typeof(PlatformUsersIndexesMigration),
            "indexes must be created after the platform_users table");

        // Dimensions
        RequireBefore(typeof(PlatformDimensionsMigration), typeof(PlatformDimensionsCodeNormMigration),
            "code_norm and its invariants require platform_dimensions table");
        RequireBefore(typeof(PlatformDimensionsMigration), typeof(PlatformDimensionsIndexesMigration),
            "dimension indexes require platform_dimensions table");

        // Dimension Sets
        RequireBefore(typeof(PlatformDimensionSetsMigration), typeof(PlatformDimensionSetItemsMigration),
            "dimension set items reference platform_dimension_sets via FK");
        RequireBefore(typeof(PlatformDimensionSetItemsMigration), typeof(PlatformDimensionSetItemsIndexesMigration),
            "dimension set item indexes require platform_dimension_set_items table");
        RequireBefore(typeof(PlatformDimensionSetItemsMigration), typeof(PlatformDimensionSetItemsConstraintsDriftRepairMigration),
            "constraint drift-repair requires platform_dimension_set_items table");

        // Append-only guards rely on the shared guard function and the target tables.
        RequireBefore(typeof(PlatformAppendOnlyGuardFunctionMigration), typeof(PlatformDimensionSetsAppendOnlyGuardMigration),
            "append-only triggers reuse ngb_forbid_mutation_of_append_only_table");
        RequireBefore(typeof(PlatformDimensionSetsMigration), typeof(PlatformDimensionSetsAppendOnlyGuardMigration),
            "append-only triggers must be installed after platform_dimension_sets exists");
        RequireBefore(typeof(PlatformDimensionSetItemsMigration), typeof(PlatformDimensionSetsAppendOnlyGuardMigration),
            "append-only triggers must be installed after platform_dimension_set_items exists");

        // AuditLog
        RequireBefore(typeof(PlatformUsersMigration), typeof(PlatformAuditEventsMigration),
            "audit events reference platform_users (actor_user_id) via FK");
        RequireBefore(typeof(PlatformAuditEventsMigration), typeof(PlatformAuditEventChangesMigration),
            "audit event changes reference platform_audit_events via FK");

        RequireBefore(typeof(PlatformAppendOnlyGuardFunctionMigration), typeof(PlatformAuditAppendOnlyGuardMigration),
            "audit append-only triggers reuse ngb_forbid_mutation_of_append_only_table");
        RequireBefore(typeof(PlatformAuditEventsMigration), typeof(PlatformAuditAppendOnlyGuardMigration),
            "append-only triggers must be installed after platform_audit_events exists");
        RequireBefore(typeof(PlatformAuditEventChangesMigration), typeof(PlatformAuditAppendOnlyGuardMigration),
            "append-only triggers must be installed after platform_audit_event_changes exists");

        RequireBefore(typeof(PlatformAuditEventsMigration), typeof(PlatformAuditIndexesMigration),
            "audit indexes require platform_audit_events table");
        RequireBefore(typeof(PlatformAuditEventsMigration), typeof(PlatformAuditPagingIndexesMigration),
            "audit paging indexes require platform_audit_events table");

        // Documents: extra (cardinality indexes are part of the DB-level contract for relationship codes).
        RequireBefore(typeof(DocumentRelationshipsMigration), typeof(DocumentRelationshipsCardinalityIndexesMigration),
            "document relationship cardinality indexes require document_relationships table");
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
