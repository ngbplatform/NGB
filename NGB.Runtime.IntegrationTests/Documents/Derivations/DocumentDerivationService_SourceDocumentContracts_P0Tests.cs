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
public sealed class DocumentDerivationService_SourceDocumentContracts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraftAsync_SourceDocumentNotFound_Throws_AndDoesNotWriteAnything()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var missingId = Guid.CreateVersion7();

        var act = () => svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: missingId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.AssertNgbError(DocumentNotFoundException.Code, "documentId");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents;"))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_relationships;"))
            .Should().Be(0);
    }

    [Fact]
    public async Task CreateDraftAsync_SourceTypeMismatch_Throws_AndDoesNotCreateDraftOrRelationships()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var sourceId = await CreateSourceDocAsync(scope.ServiceProvider, typeCode: "it_gamma");

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var act = () => svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentDerivationSourceTypeMismatchException>();
        ex.Which.AssertNgbError(
            DocumentDerivationSourceTypeMismatchException.Code,
            "derivationCode",
            "documentId",
            "expectedFromTypeCode",
            "actualFromTypeCode");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents;"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_relationships;"))
            .Should().Be(0);
    }

    private static async Task<Guid> CreateSourceDocAsync(IServiceProvider sp, string typeCode)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var id = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = typeCode,
                Number = "SRC-0001",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Posted,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = nowUtc,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return id;
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
                    .Relationships("created_from"));
        }
    }
}
