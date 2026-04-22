using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Derivations;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDerivationService_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var sourceId = await CreateSourceDocAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var act = () => svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: "DER-NTX",
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_ExternalCommit_Persists_AndExternalRollback_DoesNot()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        // Arrange: create a stable source doc outside the tested transactions
        Guid sourceId;
        await using (var scope = host.Services.CreateAsyncScope())
            sourceId = await CreateSourceDocAsync(scope.ServiceProvider);

        // Commit case
        Guid committedId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            committedId = await svc.CreateDraftAsync(
                derivationCode: "it_alpha.to_it_beta",
                createdFromDocumentId: sourceId,
                basedOnDocumentIds: null,
                dateUtc: null,
                number: "DER-COMMIT",
                manageTransaction: false,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleOrDefaultAsync<DocumentRow>(
                "SELECT type_code AS TypeCode, status AS Status FROM documents WHERE id = @id;",
                new { id = committedId });

            doc.Should().NotBeNull();
            doc!.TypeCode.Should().Be("it_beta");
            doc.Status.Should().Be((short)DocumentStatus.Draft);

            var codes = (await conn.QueryAsync<string>(
                "SELECT relationship_code_norm FROM document_relationships WHERE from_document_id = @id ORDER BY relationship_code_norm;",
                new { id = committedId })).ToArray();

            // based_on always includes createdFromDocumentId (convention)
            codes.Should().Equal(new[] { "based_on", "created_from" });
        }

        // Rollback case
        Guid rolledBackId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            rolledBackId = await svc.CreateDraftAsync(
                derivationCode: "it_alpha.to_it_beta",
                createdFromDocumentId: sourceId,
                basedOnDocumentIds: null,
                dateUtc: null,
                number: "DER-ROLLBACK",
                manageTransaction: false,
                ct: CancellationToken.None);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id = rolledBackId });

            docCount.Should().Be(0);

            var relCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @id;",
                new { id = rolledBackId });

            relCount.Should().Be(0);
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
            // Source document can be Posted - derived draft is the "from" side for relationships.
            await repo.CreateAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = "it_alpha",
                Number = "A-EXT-0001",
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

    private sealed class DocumentRow
    {
        public string TypeCode { get; init; } = "";
        public short Status { get; init; }
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
