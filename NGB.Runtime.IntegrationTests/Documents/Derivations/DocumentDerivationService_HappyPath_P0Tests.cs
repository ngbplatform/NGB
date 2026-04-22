using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Derivations;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDerivationService_HappyPath_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraft_WritesRelationships_And_HandlerCanPrefillHeader()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>();
                services.AddScoped<PrefillHeaderHandler>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var (sourceId, extraBaseId) = await CreateSourceAndBaseDocsAsync(scope.ServiceProvider);

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var rel = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        // Act
        var derivedId = await svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: new[] { extraBaseId },
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert: derived draft exists and handler applied a number.
        var derived = await docs.GetAsync(derivedId, CancellationToken.None);
        derived.Should().NotBeNull();
        derived!.TypeCode.Should().Be("it_beta");
        derived.Status.Should().Be(DocumentStatus.Draft);
        derived.Number.Should().Be("DER-0001");

        // Assert: relationships are outgoing from derived -> source/base.
        var outgoing = await rel.ListOutgoingAsync(derivedId, CancellationToken.None);

        outgoing.Should().ContainSingle(x =>
            x.ToDocumentId == sourceId && x.RelationshipCodeNorm == "created_from");

        outgoing.Should().ContainSingle(x =>
            x.ToDocumentId == sourceId && x.RelationshipCodeNorm == "based_on");

        outgoing.Should().ContainSingle(x =>
            x.ToDocumentId == extraBaseId && x.RelationshipCodeNorm == "based_on");

        outgoing.Should().HaveCount(3);
    }

    private static async Task<(Guid SourceId, Guid ExtraBaseId)> CreateSourceAndBaseDocsAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var sourceId = Guid.CreateVersion7();
        var extraBaseId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            // Source document can be Posted - derived draft is the "from" side for relationships.
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
                Id = extraBaseId,
                TypeCode = "it_alpha",
                Number = "A-0002",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (sourceId, extraBaseId);
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
                    .Relationships("created_from", "based_on")
                    .Handler<PrefillHeaderHandler>());
        }
    }

    private sealed class PrefillHeaderHandler(IDocumentRepository documents) : IDocumentDerivationHandler
    {
        public async Task ApplyAsync(DocumentDerivationContext context, CancellationToken ct)
        {
            await documents.UpdateDraftHeaderAsync(
                documentId: context.TargetDraft.Id,
                number: "DER-0001",
                dateUtc: context.TargetDraft.DateUtc,
                updatedAtUtc: DateTime.UtcNow,
                ct: ct);
        }
    }
}