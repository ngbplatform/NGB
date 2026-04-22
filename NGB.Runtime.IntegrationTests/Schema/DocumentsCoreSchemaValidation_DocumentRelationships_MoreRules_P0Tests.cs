using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P0: Rule-by-rule coverage for Documents core schema validation (document_relationships).
/// These tests intentionally break a single contract invariant and ensure the validator
/// reports the exact missing contract element.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_MoreRules_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenFromDocumentFkConstraintIsRenamed_ThrowsMissingExpectedConstraintName()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Keep the FK semantics, but drift the constraint name.
        await ExecuteSqlAsync(
            Fixture.ConnectionString,
            """
            ALTER TABLE public.document_relationships
                DROP CONSTRAINT IF EXISTS fk_document_relationships_from_document;

            ALTER TABLE public.document_relationships
                ADD CONSTRAINT fk_docrel_from_document
                FOREIGN KEY (from_document_id) REFERENCES public.documents(id);
            """);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*fk_document_relationships_from_document*");
    }

    [Fact]
    public async Task ValidateAsync_WhenTripletUniqueConstraintIsMissingButIndexExists_ThrowsMissingConstraintName()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Drop the UNIQUE CONSTRAINT, but re-create the UNIQUE INDEX with the same contract name
        // so only the constraint-name contract fails.
        await ExecuteSqlAsync(
            Fixture.ConnectionString,
            """
            ALTER TABLE public.document_relationships
                DROP CONSTRAINT IF EXISTS ux_document_relationships_triplet;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_document_relationships_triplet
                ON public.document_relationships (from_document_id, to_document_id, relationship_code_norm);
            """);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*ux_document_relationships_triplet*")
            .WithMessage("*Missing constraint*");
    }

    [Fact]
    public async Task ValidateAsync_WhenBuiltInCardinalityPartialUniqueIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ExecuteSqlAsync(
            Fixture.ConnectionString,
            "DROP INDEX IF EXISTS public.ux_docrel_from_rev_of;");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*ux_docrel_from_rev_of*");
    }

    private static async Task ExecuteSqlAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
