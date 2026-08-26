using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Migrations;

[Collection(SchemaPostgresCollection.Name)]
public sealed class PlatformSearchTrigramIndexes_P1Tests(SchemaPostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Migration_installs_extension_helper_and_all_platform_indexes()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();

        var extensionInstalled = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm');");
        var helperInstalled = await connection.ExecuteScalarAsync<bool>(
            "SELECT to_regprocedure('public.ngb_install_search_trigram_indexes(text[])') IS NOT NULL;");
        var indexes = (await connection.QueryAsync<string>(
            """
            SELECT indexname
              FROM pg_indexes
             WHERE schemaname = 'public'
               AND indexname = ANY(@Names)
             ORDER BY indexname;
            """,
            new
            {
                Names = new[]
                {
                    "ix_documents_number_trgm",
                    "ix_accounting_accounts_code_trgm",
                    "ix_accounting_accounts_name_trgm",
                    "ix_doc_gje_reason_code_trgm",
                    "ix_doc_gje_memo_trgm",
                    "ix_doc_gje_external_reference_trgm"
                }
            })).ToArray();

        extensionInstalled.Should().BeTrue();
        helperInstalled.Should().BeTrue();
        indexes.Should().HaveCount(6);
    }
}
