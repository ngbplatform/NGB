using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Derivations;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDerivationService_BasedOnNormalization_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraftAsync_WhenBasedOnIsNull_WritesBasedOnForCreatedFromOnly()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var sourceId = await CreateSourceDocAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        // Act
        var derivedId = await svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: "DER-NULL",
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var rows = (await conn.QueryAsync<RelRow>(
            """
            SELECT relationship_code_norm AS Code, to_document_id AS ToDocumentId
            FROM document_relationships
            WHERE from_document_id = @id
            ORDER BY relationship_code_norm, to_document_id;
            """,
            new { id = derivedId })).ToArray();

        rows.Should().HaveCount(2);

        rows.Should().ContainSingle(x => x.Code == "created_from" && x.ToDocumentId == sourceId);
        rows.Should().ContainSingle(x => x.Code == "based_on" && x.ToDocumentId == sourceId);
    }

    [Fact]
    public async Task CreateDraftAsync_BasedOn_Normalizes_Deduplicates_IgnoresEmpty_AndDoesNotDuplicateCreatedFrom()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var (sourceId, base1Id, base2Id) = await CreateSourceAndTwoBaseDocsAsync(scope.ServiceProvider);

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        // Act: include Guid.Empty, duplicates and createdFrom in the based_on list.
        var derivedId = await svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: new[]
            {
                base2Id,
                Guid.Empty,
                base1Id,
                base1Id,
                sourceId,
                base2Id,
                Guid.Empty,
                sourceId
            },
            dateUtc: null,
            number: "DER-NORM",
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var rows = (await conn.QueryAsync<RelRow>(
            """
            SELECT relationship_code_norm AS Code, to_document_id AS ToDocumentId
            FROM document_relationships
            WHERE from_document_id = @id
            ORDER BY relationship_code_norm, to_document_id;
            """,
            new { id = derivedId })).ToArray();

        // created_from is always derived -> createdFrom.
        rows.Should().ContainSingle(x => x.Code == "created_from" && x.ToDocumentId == sourceId);

        // based_on set must be: createdFrom + unique non-empty ids.
        var basedOnTargets = rows
            .Where(x => x.Code == "based_on")
            .Select(x => x.ToDocumentId)
            .OrderBy(x => x)
            .ToArray();

        basedOnTargets.Should().Equal(new[] { base1Id, base2Id, sourceId }.OrderBy(x => x));

        // Total outgoing relationships: 1 (created_from) + 3 (based_on unique set)
        rows.Should().HaveCount(4);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenBasedOnContainsMissingDocument_Throws_AndRollsBackEverything()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        Guid sourceId;
        await using (var scope = host.Services.CreateAsyncScope())
            sourceId = await CreateSourceDocAsync(scope.ServiceProvider);

        // Baseline counts
        int baselineDocs;
        int baselineRels;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineDocs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents;");
            baselineRels = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_relationships;");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

            var missing = Guid.CreateVersion7();

            var act = () => svc.CreateDraftAsync(
                derivationCode: "it_alpha.to_it_beta",
                createdFromDocumentId: sourceId,
                basedOnDocumentIds: new[] { missing },
                dateUtc: null,
                number: "DER-MISS",
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
            ex.Which.AssertNgbError(DocumentNotFoundException.Code, "documentId");
}

        // Assert rollback
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents;");
            docs.Should().Be(baselineDocs, "derived draft must be rolled back when based_on contains a missing document");

            var rels = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_relationships;");
            rels.Should().Be(baselineRels, "relationship inserts must be rolled back together with the transaction");

            // Extra defense: derived type should not exist.
            var derivedCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE type_code = 'it_beta';");
            derivedCount.Should().Be(0);
        }
    }

    private static async Task<Guid> CreateSourceDocAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var sourceId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Posted,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = nowUtc,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return sourceId;
    }

    private static async Task<(Guid SourceId, Guid Base1Id, Guid Base2Id)> CreateSourceAndTwoBaseDocsAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var sourceId = Guid.CreateVersion7();
        var base1Id = Guid.CreateVersion7();
        var base2Id = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Posted,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = nowUtc,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = base1Id,
                TypeCode = "it_alpha",
                Number = "A-0002",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = base2Id,
                TypeCode = "it_alpha",
                Number = "A-0003",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (sourceId, base1Id, base2Id);
    }

    private sealed class RelRow
    {
        public string Code { get; init; } = string.Empty;
        public Guid ToDocumentId { get; init; }
    }

    private sealed class TestDerivationContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocumentDerivation(
                derivationCode: "it_alpha.to_it_beta",
                configure: d => d
                    .Name("Create IT Beta")
                    .From("it_alpha")
                    .To("it_beta")
                    .Relationships("created_from", "based_on"));
        }
    }
}
