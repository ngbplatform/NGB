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
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_DriftRepair_RuleByRule_P0_2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenCardinalityIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ux_docrel_from_supersedes");

        await AssertDocumentsCoreValidationFailsAsync("*ux_docrel_from_supersedes*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenCriticalCheckConstraintMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropConstraintAsync(Fixture.ConnectionString, "document_relationships", "ck_document_relationships_not_self");

        await AssertDocumentsCoreValidationFailsAsync("*ck_document_relationships_not_self*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await AssertDocumentsCoreValidationSucceedsAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenTripletUniquenessConstraintMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        // NOTE: dropping the UNIQUE constraint also drops the underlying unique index.
        await DropConstraintAsync(Fixture.ConnectionString, "document_relationships", "ux_document_relationships_triplet");

        await AssertDocumentsCoreValidationFailsAsync("*ux_document_relationships_triplet*");

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

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE public.{tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
