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
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_DriftRepair_RuleByRule_P0_3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenDraftGuardTriggerMissing_FailsThenBootstrapperRepairs()
    {
        await DropTriggerAsync(Fixture.ConnectionString, "document_relationships", "trg_document_relationships_draft_guard");

        await AssertDocumentsCoreValidationFailsAsync("*Missing trigger 'trg_document_relationships_draft_guard'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenRelationshipCodeTrimmedConstraintMissing_FailsThenBootstrapperRepairs()
    {
        await DropConstraintAsync(Fixture.ConnectionString, "document_relationships", "ck_document_relationships_code_trimmed");

        await AssertDocumentsCoreValidationFailsAsync("*ck_document_relationships_code_trimmed*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenPerformanceIndexMissing_FailsThenBootstrapperRepairs()
    {
        await DropIndexAsync(Fixture.ConnectionString, "ix_docrel_to_code_created_id");

        await AssertDocumentsCoreValidationFailsAsync("*ix_docrel_to_code_created_id*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenForeignKeyConstraintToDocumentMissing_FailsThenBootstrapperRepairs()
    {
        await DropConstraintAsync(Fixture.ConnectionString, "document_relationships", "fk_document_relationships_to_document");

        await AssertDocumentsCoreValidationFailsAsync("*fk_document_relationships_to_document*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    private async Task AssertDocumentsCoreValidationFailsAsync(string expectedMessagePattern)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage(expectedMessagePattern);
    }

    private async Task AssertDocumentsCoreValidationSucceedsAsync()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
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

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE public.{tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
