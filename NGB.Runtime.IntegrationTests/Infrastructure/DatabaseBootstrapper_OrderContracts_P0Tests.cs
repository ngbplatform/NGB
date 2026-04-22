using System.Reflection;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Accounting;
using NGB.PostgreSql.Migrations.Documents;
using NGB.PostgreSql.Migrations.Platform;
using NGB.PostgreSql.Migrations.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class DatabaseBootstrapper_OrderContracts_P0Tests
{
    [Fact]
    public void Bootstrapper_OrderMustRespect_HardDependencies()
    {
        var wired = BuildBootstrapperSet().Select(x => x.GetType()).ToList();

        int IndexOf(System.Type t)
        {
            var idx = wired.IndexOf(t);
            idx.Should().BeGreaterThanOrEqualTo(0, $"bootstrapper must include {t.Name}");
            return idx;
        }

        void RequireBefore(System.Type before, System.Type after, string because)
            => IndexOf(before).Should().BeLessThan(IndexOf(after), because);

        IndexOf(typeof(PlatformAppendOnlyGuardFunctionMigration)).Should().Be(0,
            "PlatformAppendOnlyGuardFunctionMigration must be first");

        RequireBefore(typeof(PlatformDimensionsMigration), typeof(AccountingAccountDimensionRulesMigration),
            "accounting dimension rules may reference platform_dimensions");
        RequireBefore(typeof(PlatformDimensionSetsMigration), typeof(AccountingRegisterDimensionSetForeignKeysMigration),
            "accounting register FK to platform_dimension_sets must be created after platform dimension sets");
        RequireBefore(typeof(PlatformDimensionSetItemsMigration), typeof(AccountingRegisterDimensionSetForeignKeysMigration),
            "dimension set items must exist before FK install");

        RequireBefore(typeof(PlatformUsersMigration), typeof(PlatformAuditEventsMigration),
            "audit events may reference platform_users");

        RequireBefore(typeof(AccountingPostingStateMigration), typeof(AccountingPostingOperationHistoryMigration),
            "immutable posting history must be created after the accounting posting state table contract exists");

        RequireBefore(typeof(DocumentsMigration), typeof(GeneralJournalEntryMigration),
            "typed document tables must be created after documents registry");
        RequireBefore(typeof(DocumentsMigration), typeof(DocumentOperationStateMigration),
            "document operation state references documents via FK");
        RequireBefore(typeof(DocumentsMigration), typeof(DocumentOperationHistoryMigration),
            "document operation history references documents via FK");
        RequireBefore(typeof(DocumentOperationStateMigration), typeof(DocumentOperationHistoryIndexesMigration),
            "document operation indexes must be created after the tables");
        RequireBefore(typeof(DocumentOperationHistoryMigration), typeof(DocumentOperationHistoryIndexesMigration),
            "document operation indexes must be created after the history table");
        RequireBefore(typeof(DocumentsMigration), typeof(DocumentRelationshipsMigration),
            "document relationships reference documents via FK");
        RequireBefore(typeof(DocumentRelationshipsMigration), typeof(DocumentRelationshipsIndexesMigration),
            "document relationship indexes must be created after the table");
        RequireBefore(typeof(DocumentRelationshipsMigration), typeof(DocumentRelationshipsDraftGuardMigration),
            "document relationship trigger must be installed after the table");
        RequireBefore(typeof(DocumentsMigration), typeof(PostedDocumentImmutabilityGuardMigration),
            "immutability triggers must be installed after tables");
        RequireBefore(typeof(DocumentsMigration), typeof(PostedDocumentHeaderImmutabilityGuardMigration),
            "header immutability trigger references documents table");

        RequireBefore(typeof(DocumentsMigration), typeof(ReferenceRegisterWriteLogHistoryMigration),
            "reference register write history references documents via FK");
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
