using FluentAssertions;
using NGB.PostgreSql.Migrations.Platform;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class PlatformSearchTrigramIndexesMigrationTests
{
    [Fact]
    public void Generate_installs_trigram_support_and_all_platform_search_indexes()
    {
        var migration = new PlatformSearchTrigramIndexesMigration();

        var sql = migration.Generate();

        migration.Name.Should().Be("platform_search_trigram_indexes");
        sql.Should()
            .Contain("CREATE EXTENSION IF NOT EXISTS pg_trgm")
            .And.Contain("ngb_install_search_trigram_indexes")
            .And.Contain("starts_with(c.table_name, prefix.value)")
            .And.Contain("JOIN pg_namespace n ON n.oid = e.extnamespace")
            .And.Contain("USING gin (display %I.gin_trgm_ops)")
            .And.Contain("ix_documents_number_trgm")
            .And.Contain("ix_accounting_accounts_code_trgm")
            .And.Contain("ix_accounting_accounts_name_trgm")
            .And.Contain("ix_doc_gje_reason_code_trgm")
            .And.Contain("ix_doc_gje_memo_trgm")
            .And.Contain("ix_doc_gje_external_reference_trgm");
    }
}
