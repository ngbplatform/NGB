using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_DriftRepair_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenDraftGuardFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "document_relationships",
            triggerName: "trg_document_relationships_draft_guard");

        await DropFunctionAsync(
            Fixture.ConnectionString,
            functionName: "ngb_enforce_document_relationships_draft_from_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<DocumentSchemaValidationException>()
                .WithMessage("*Missing function 'ngb_enforce_document_relationships_draft_from_document'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenForeignKeyConstraintMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropConstraintAsync(
            Fixture.ConnectionString,
            tableName: "document_relationships",
            constraintName: "fk_document_relationships_from_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<DocumentSchemaValidationException>()
                .WithMessage("*fk_document_relationships_from_document*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenCardinalityIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(
            Fixture.ConnectionString,
            indexName: "ux_docrel_from_created_from");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<DocumentSchemaValidationException>()
                .WithMessage("*ux_docrel_from_created_from*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenDocumentRelationshipsTableMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTableAsync(
            Fixture.ConnectionString,
            tableName: "document_relationships");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<DocumentSchemaValidationException>()
                .WithMessage("*Missing table 'document_relationships'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropFunctionAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // All platform guard functions are in public schema and have no args.
        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS {functionName}();", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTableAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {tableName} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
