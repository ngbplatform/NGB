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
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenSchemaIsIntact_Succeeds()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenDraftGuardTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "document_relationships",
            triggerName: "trg_document_relationships_draft_guard");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DocumentSchemaValidationException>();
        ex.Which.AssertNgbError(DocumentSchemaValidationException.Code);
        ex.Which.Message.Should().Contain("trg_document_relationships_draft_guard");
    }

    [Fact]
    public async Task ValidateAsync_WhenCriticalCheckConstraintMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropConstraintAsync(
            Fixture.ConnectionString,
            tableName: "document_relationships",
            constraintName: "ck_document_relationships_code_trimmed");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DocumentSchemaValidationException>();
        ex.Which.AssertNgbError(DocumentSchemaValidationException.Code);
        ex.Which.Message.Should().Contain("ck_document_relationships_code_trimmed");
    }

    [Fact]
    public async Task ValidateAsync_WhenCodeNormIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(
            Fixture.ConnectionString,
            indexName: "ix_docrel_from_code_created_id");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DocumentSchemaValidationException>();
        ex.Which.AssertNgbError(DocumentSchemaValidationException.Code);
        ex.Which.Message.Should().Contain("ix_docrel_from_code_created_id");
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

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
