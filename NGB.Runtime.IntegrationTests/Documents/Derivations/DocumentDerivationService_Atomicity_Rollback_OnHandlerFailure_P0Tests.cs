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
public sealed class DocumentDerivationService_Atomicity_Rollback_OnHandlerFailure_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HandlerFailure_RollsBack_DraftAndRelationships()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>();
                services.AddSingleton<ThrowingHandler>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var sourceId = await CreateSourceDocAsync(scope.ServiceProvider);

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var rel = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        var handler = scope.ServiceProvider.GetRequiredService<ThrowingHandler>();

        // Act
        var act = async () => await svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta.throw",
            createdFromDocumentId: sourceId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*boom*");

        handler.TargetDraftId.Should().NotBeNull();
        var targetId = handler.TargetDraftId!.Value;

        // Assert: draft header row is rolled back.
        (await docs.GetAsync(targetId, CancellationToken.None)).Should().BeNull();

        // Assert: no relationships were persisted.
        (await rel.ListOutgoingAsync(targetId, CancellationToken.None)).Should().BeEmpty();
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

    private sealed class TestDerivationContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocumentDerivation(
                derivationCode: "it_alpha.to_it_beta.throw",
                configure: d => d
                    .Name("Create IT Beta (Throw)")
                    .From("it_alpha")
                    .To("it_beta")
                    .Relationships("created_from")
                    .Handler<ThrowingHandler>());
        }
    }

    private sealed class ThrowingHandler : IDocumentDerivationHandler
    {
        public Guid? TargetDraftId { get; private set; }

        public Task ApplyAsync(DocumentDerivationContext context, CancellationToken ct)
        {
            TargetDraftId = context.TargetDraft.Id;
            throw new NotSupportedException("boom");
        }
    }
}