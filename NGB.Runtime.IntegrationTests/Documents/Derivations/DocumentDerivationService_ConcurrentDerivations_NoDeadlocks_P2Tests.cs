using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
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
public sealed class DocumentDerivationService_ConcurrentDerivations_NoDeadlocks_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    /// <summary>
    /// P2: concurrency / deadlock-safety contract.
    /// If deterministic locking regresses, this scenario is a classic deadlock:
    ///  - T1 derives from A and is based_on B (locks A then B)
    ///  - T2 derives from B and is based_on A (locks B then A)
    /// The service must normalize/lock deterministically and therefore complete without deadlocks.
    /// </summary>
    [Fact]
    public async Task CreateDraftAsync_ConcurrentCrossDerivations_DoNotDeadlock_AndRelationshipsAreConsistent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDerivationContributor>());

        Guid docA;
        Guid docB;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            docA = await CreateSourceDocAsync(scope.ServiceProvider, number: "A-0001");
            docB = await CreateSourceDocAsync(scope.ServiceProvider, number: "A-0002");
        }

        // Run the contention scenario multiple times to increase confidence without making the test too heavy.
        const int iterations = 5;

        var derivedToSource = new Dictionary<Guid, Guid>();

        for (var i = 1; i <= iterations; i++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var t1 = Task.Run(async () =>
            {
                await start.Task;
                await using var scope = host.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

                var derivedId = await svc.CreateDraftAsync(
                    derivationCode: "it_alpha.to_it_beta",
                    createdFromDocumentId: docA,
                    basedOnDocumentIds: new[] { docB, Guid.Empty, docB }, // intentionally noisy
                    dateUtc: null,
                    number: $"DER-A-{i:00}",
                    manageTransaction: true,
                    ct: cts.Token);

                return derivedId;
            }, cts.Token);

            var t2 = Task.Run(async () =>
            {
                await start.Task;
                await using var scope = host.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

                var derivedId = await svc.CreateDraftAsync(
                    derivationCode: "it_alpha.to_it_beta",
                    createdFromDocumentId: docB,
                    basedOnDocumentIds: new[] { docA }, // opposite direction
                    dateUtc: null,
                    number: $"DER-B-{i:00}",
                    manageTransaction: true,
                    ct: cts.Token);

                return derivedId;
            }, cts.Token);

            start.SetResult();

            Guid[] ids;
            try
            {
                ids = await Task.WhenAll(t1, t2).WaitAsync(cts.Token);
            }
            catch (PostgresException ex) when (ex.SqlState == "40P01")
            {
                throw new Xunit.Sdk.XunitException("Deadlock detected (40P01). Deterministic locking order regressed.", ex);
            }

            ids.Should().HaveCount(2);
            ids[0].Should().NotBe(Guid.Empty);
            ids[1].Should().NotBe(Guid.Empty);
            ids[0].Should().NotBe(ids[1]);

            derivedToSource[ids[0]] = docA;
            derivedToSource[ids[1]] = docB;
        }

        // Verify all derived drafts and their relationships in one query.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var derivedIds = derivedToSource.Keys.ToArray();

        var docs = (await conn.QueryAsync<DocRow>(
            """
            SELECT id AS Id, type_code AS TypeCode, number AS Number, status AS Status
            FROM documents
            WHERE id = ANY(@ids);
            """,
            new { ids = derivedIds })).ToArray();

        docs.Should().HaveCount(derivedIds.Length);
        docs.Should().OnlyContain(d => d.TypeCode == "it_beta");
        docs.Should().OnlyContain(d => d.Status == DocumentStatus.Draft);

        var rels = (await conn.QueryAsync<RelRow>(
            """
            SELECT from_document_id AS FromDocumentId,
                   relationship_code_norm AS RelationshipCodeNorm,
                   to_document_id AS ToDocumentId
            FROM document_relationships
            WHERE from_document_id = ANY(@ids)
            ORDER BY from_document_id, relationship_code_norm, to_document_id;
            """,
            new { ids = derivedIds })).ToArray();

        foreach (var derivedId in derivedIds)
        {
            var sourceId = derivedToSource[derivedId];

            var outgoing = rels.Where(r => r.FromDocumentId == derivedId).ToArray();

            // Expected per derived:
            // - created_from -> source
            // - based_on -> {source, other}
            outgoing.Should().HaveCount(3, "derivation must write exactly 1 created_from + 2 based_on relationships");

            outgoing.Should().ContainSingle(r =>
                r.RelationshipCodeNorm == "created_from" && r.ToDocumentId == sourceId);

            var basedOnTargets = outgoing
                .Where(r => r.RelationshipCodeNorm == "based_on")
                .Select(r => r.ToDocumentId)
                .OrderBy(x => x)
                .ToArray();

            basedOnTargets.Should().Equal(new[] { docA, docB }.OrderBy(x => x));
        }
    }

    private static async Task<Guid> CreateSourceDocAsync(IServiceProvider sp, string number)
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
                TypeCode = "it_alpha",
                Number = number,
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

    private sealed class RelRow
    {
        public Guid FromDocumentId { get; init; }
        public string RelationshipCodeNorm { get; init; } = string.Empty;
        public Guid ToDocumentId { get; init; }
    }

    private sealed class DocRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = string.Empty;
        public string? Number { get; init; }
        public DocumentStatus Status { get; init; }
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
