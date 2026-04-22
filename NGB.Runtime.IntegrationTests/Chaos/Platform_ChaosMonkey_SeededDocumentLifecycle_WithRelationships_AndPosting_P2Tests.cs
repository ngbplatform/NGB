using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Chaos;

[Collection(PostgresCollection.Name)]
public sealed class Platform_ChaosMonkey_SeededDocumentLifecycle_WithRelationships_AndPosting_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|chaos-doc-life-p2";
    private static readonly DateTime BaseDateUtc = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    public async Task SeededOperations_DoNotViolateCoreInvariants(int seed)
    {
        // Seed the minimal CoA BEFORE building the host.
        // This avoids any chance of a singleton or background initialization caching an empty CoA snapshot.
        await SeedMinimalChartOfAccountsWithoutAuditAsync(Fixture.ConnectionString);

        using var host = CreateHostWithActor(Fixture.ConnectionString);

        var baselineAuditEvents = await CountAuditEventsAsync(Fixture.ConnectionString);

        var rng = new Random(seed);
        var state = new ShadowState();
        var expectedAuditEvents = 0;

        // Prime with a few documents to enable relationship + posting operations early.
        for (var i = 0; i < 3; i++)
        {
            var didWork = await CreateDraftAsync(host, state, rng);
            didWork.Should().BeTrue("initial seeds must create drafts");
            expectedAuditEvents++;
        }

        const int steps = 80;
        for (var step = 0; step < steps; step++)
        {
            var op = PickOperation(rng, state);

            // For fail-fast / no-op assertions we track audit count around each operation.
            var auditBefore = await CountAuditEventsAsync(Fixture.ConnectionString);

            var didWork = false;
            Exception? failure = null;
            try
            {
                didWork = op switch
                {
                    ChaosOp.CreateDraft => await CreateDraftAsync(host, state, rng),
                    ChaosOp.UpdateDraft => await UpdateDraftAsync(host, state, rng),
                    ChaosOp.DeleteDraft => await DeleteDraftAsync(host, state, rng),
                    ChaosOp.MarkForDeletion => await MarkForDeletionAsync(host, state, rng),
                    ChaosOp.UnmarkForDeletion => await UnmarkForDeletionAsync(host, state, rng),
                    ChaosOp.CreateRelationship => await CreateRelationshipAsync(host, state, rng),
                    ChaosOp.DeleteRelationship => await DeleteRelationshipAsync(host, state, rng),
                    ChaosOp.Post => await PostAsync(host, state, rng),
                    ChaosOp.Unpost => await UnpostAsync(host, state, rng),
                    ChaosOp.Repost => await RepostAsync(host, state, rng),
                    _ => throw new NgbArgumentOutOfRangeException(nameof(op), op, "Unknown operation")
                };
            }
            catch (NgbException ex)
            {
                failure = ex;
                // Negative cases are part of the chaos run. They must be fail-fast and must not write audit.
                didWork = false;
            }
            catch (Exception ex)
            {
                failure = ex;
                // Unexpected failures are allowed to surface, but must not emit audit side effects.
                didWork = false;
            }

            var auditAfter = await CountAuditEventsAsync(Fixture.ConnectionString);

            if (didWork)
            {
                (auditAfter - auditBefore).Should().Be(1, $"seed={seed}, step={step}, op={op}: each successful mutating operation must emit exactly one audit event");
                expectedAuditEvents++;
            }
            else
            {
                auditAfter.Should().Be(auditBefore, $"seed={seed}, step={step}, op={op}, ex={failure?.GetType().Name}: no-op or fail-fast operations must not emit audit");
            }

            // Validate core invariants periodically to narrow down failures.
            if (step % 10 == 0)
                await AssertCoreInvariantsAsync(Fixture.ConnectionString, state, $"seed={seed}, step={step}, op={op}");
        }

        await AssertCoreInvariantsAsync(Fixture.ConnectionString, state, $"seed={seed}, step=final, op=final");

        var finalAuditEvents = await CountAuditEventsAsync(Fixture.ConnectionString);
        finalAuditEvents.Should().Be(baselineAuditEvents + expectedAuditEvents);

        // With a fixed actor, any successful audit write must upsert exactly one platform_users row.
        var usersForActor = await CountUsersForSubjectAsync(Fixture.ConnectionString, AuthSubject);
        usersForActor.Should().Be(1);
    }

    private static IHost CreateHostWithActor(string cs)
    {
        return IntegrationHostFactory.Create(cs, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ =>
                new FixedCurrentActorContext(
                    new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "chaos.monkey@example.com",
                        DisplayName: "Chaos Monkey")));
        });
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private static async Task SeedMinimalChartOfAccountsWithoutAuditAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Keep the seed minimal: just enough to allow a deterministic posting action.
        // Use INSERT .. ON CONFLICT DO NOTHING to be idempotent within the test.
        const string sql = """
                           INSERT INTO accounting_accounts
                           (account_id, code, name, account_type, statement_section, negative_balance_policy)
                           VALUES
                           (@cashId, @cashCode, 'Cash', @cashType, @cashSection, @policy),
                           (@incomeId, @incomeCode, 'Sales', @incomeType, @incomeSection, @policy)
                           ON CONFLICT (account_id) DO NOTHING;
                           """;

        await conn.ExecuteAsync(
            sql,
            new
            {
                cashId = DeterministicGuid.Create("IT|Account|50"),
                incomeId = DeterministicGuid.Create("IT|Account|90.1"),
                cashCode = "50",
                incomeCode = "90.1",
                cashType = AccountType.Asset,
                incomeType = AccountType.Income,
                cashSection = StatementSection.Assets,
                incomeSection = StatementSection.Income,
                policy = NegativeBalancePolicy.Allow
            });
    }

    private static ChaosOp PickOperation(Random rng, ShadowState state)
    {
        // Bias toward operations that exercise cross-cutting behaviors.
        // If the state is small, create more drafts.
        if (state.Documents.Count < 2)
            return ChaosOp.CreateDraft;

        var roll = rng.Next(100);
        return roll switch
        {
            < 15 => ChaosOp.CreateDraft,
            < 30 => ChaosOp.UpdateDraft,
            < 40 => ChaosOp.DeleteDraft,
            < 50 => ChaosOp.MarkForDeletion,
            < 60 => ChaosOp.UnmarkForDeletion,
            < 75 => rng.Next(2) == 0 ? ChaosOp.CreateRelationship : ChaosOp.DeleteRelationship,
            < 85 => ChaosOp.Post,
            < 93 => ChaosOp.Unpost,
            _ => ChaosOp.Repost
        };
    }

    private static async Task<bool> CreateDraftAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count >= 10)
            return false; // throttle

        var number = $"D-{state.NextNumber:00000}";
        state.NextNumber++;

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var dateUtc = BaseDateUtc.AddDays(rng.Next(0, 10));
        var id = await drafts.CreateDraftAsync(
            typeCode: "it_doc_a",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);

        state.Documents[id] = new ShadowDoc(
            Id: id,
            Number: number,
            DateUtc: dateUtc,
            Status: DocumentStatus.Draft,
            HasPostLog: false,
            HasUnpostLog: false,
            HasRepostLog: false);

        return true;
    }

    private static async Task<bool> UpdateDraftAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);

        // Randomly choose a real change or an explicit no-op (same values).
        var change = rng.Next(100) < 70;
        var newNumber = change ? $"{doc.Number}-U{rng.Next(0, 100)}" : doc.Number;
        var newDate = change ? doc.DateUtc.AddDays(1) : doc.DateUtc;

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var didWork = await drafts.UpdateDraftAsync(
            documentId: doc.Id,
            number: newNumber,
            dateUtc: newDate,
            manageTransaction: true,
            ct: CancellationToken.None);

        if (didWork)
        {
            state.Documents[doc.Id] = doc with { Number = newNumber, DateUtc = newDate };
        }

        return didWork;
    }

    private static async Task<bool> DeleteDraftAsync(IHost host, ShadowState state, Random rng)
    {
        // If we have no docs, try deleting a random missing id (idempotent no-op).
        var id = state.Documents.Count == 0 ? Guid.CreateVersion7() : Pick(state.Documents.Keys.ToList(), rng);

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var didWork = await drafts.DeleteDraftAsync(
            documentId: id,
            manageTransaction: true,
            ct: CancellationToken.None);

        if (didWork)
        {
            state.Documents.Remove(id);
            state.Relationships.RemoveWhere(r => r.From == id || r.To == id);
        }

        return didWork;
    }

    private static async Task<bool> MarkForDeletionAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);

        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await posting.MarkForDeletionAsync(doc.Id, CancellationToken.None);

        if (doc.Status == DocumentStatus.MarkedForDeletion)
            return false; // service is idempotent no-op

        state.Documents[doc.Id] = doc with { Status = DocumentStatus.MarkedForDeletion };
        return true;
    }

    private static async Task<bool> UnmarkForDeletionAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);

        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await posting.UnmarkForDeletionAsync(doc.Id, CancellationToken.None);

        if (doc.Status != DocumentStatus.MarkedForDeletion)
            return false; // idempotent no-op

        state.Documents[doc.Id] = doc with { Status = DocumentStatus.Draft };
        return true;
    }

    private static async Task<bool> CreateRelationshipAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count < 2)
            return false;

        // Relationship service requires from-document to be Draft.
        var candidates = state.Documents.Values.Where(d => d.Status == DocumentStatus.Draft).ToList();
        if (candidates.Count == 0)
            return false;

        var from = Pick(candidates, rng);
        if (state.Relationships.Any(r => r.From == from.Id && r.CodeNorm == "created_from"))
            return false; // would violate maxOutgoingPerFrom=1

        var to = Pick(state.Documents.Values.Where(d => d.Id != from.Id).ToList(), rng);

        await using var scope = host.Services.CreateAsyncScope();
        var rel = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var didWork = await rel.CreateAsync(
            fromDocumentId: from.Id,
            toDocumentId: to.Id,
            relationshipCode: "created_from",
            manageTransaction: true,
            ct: CancellationToken.None);

        if (didWork)
            state.Relationships.Add(new ShadowRelationship(from.Id, to.Id, "created_from"));

        return didWork;
    }

    private static async Task<bool> DeleteRelationshipAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Relationships.Count == 0)
            return false;

        // Delete requires from-document to be Draft.
        var deletable = state.Relationships
            .Where(r => state.Documents.TryGetValue(r.From, out var doc) && doc.Status == DocumentStatus.Draft)
            .ToList();

        if (deletable.Count == 0)
            return false;

        var pick = Pick(deletable, rng);

        await using var scope = host.Services.CreateAsyncScope();
        var rel = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var didWork = await rel.DeleteAsync(
            fromDocumentId: pick.From,
            toDocumentId: pick.To,
            relationshipCode: pick.CodeNorm,
            manageTransaction: true,
            ct: CancellationToken.None);

        if (didWork)
            state.Relationships.Remove(pick);

        return didWork;
    }

    private static async Task<bool> PostAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);
        if (doc.Status != DocumentStatus.Draft)
        {
            // PostAsync is an idempotent no-op for Posted and a fail-fast for MarkedForDeletion.
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(doc.Id, PostAction(doc.Id, rng), CancellationToken.None);
            return false;
        }

        // Post becomes a no-op only while the completed Post state remains current.
        if (doc.HasPostLog)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(doc.Id, PostAction(doc.Id, rng), CancellationToken.None);
            return false;
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(doc.Id, PostAction(doc.Id, rng), CancellationToken.None);
        }

        state.Documents[doc.Id] = doc with
        {
            Status = DocumentStatus.Posted,
            HasPostLog = true,
            HasUnpostLog = false
        };
        return true;
    }

    private static async Task<bool> UnpostAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);

        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await posting.UnpostAsync(doc.Id, CancellationToken.None);

        if (doc.Status != DocumentStatus.Posted)
            return false; // draft => explicit no-op; marked => exception handled by caller

        if (doc.HasUnpostLog)
            return false; // completed Unpost state is still current

        state.Documents[doc.Id] = doc with
        {
            Status = DocumentStatus.Draft,
            HasPostLog = false,
            HasUnpostLog = true,
            HasRepostLog = false
        };
        return true;
    }

    private static async Task<bool> RepostAsync(IHost host, ShadowState state, Random rng)
    {
        if (state.Documents.Count == 0)
            return false;

        var doc = Pick(state.Documents.Values, rng);

        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await posting.RepostAsync(doc.Id, PostAction(doc.Id, rng), CancellationToken.None);

        if (doc.Status != DocumentStatus.Posted)
            return false;

        if (doc.HasRepostLog)
            return false;

        state.Documents[doc.Id] = doc with { HasRepostLog = true };
        return true;
    }

    private static Func<IAccountingPostingContext, CancellationToken, Task> PostAction(Guid documentId, Random rng)
    {
        var amount = rng.Next(1, 10) * 10m;
        return async (ctx, ct) =>
        {
            var coa = await ctx.GetChartOfAccountsAsync(ct);
            var debit = coa.Get("50");
            var credit = coa.Get("90.1");

            ctx.Post(
                documentId: documentId,
                period: BaseDateUtc,
                debit: debit,
                credit: credit,
                amount: amount);
        };
    }

    private static async Task AssertCoreInvariantsAsync(string cs, ShadowState state, string context)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // 1) Shadow documents must exist and have expected status.
        foreach (var d in state.Documents.Values)
        {
            var row = await conn.QuerySingleOrDefaultAsync<DocRow>(
                "SELECT id AS Id, status AS Status, posted_at_utc AS PostedAtUtc FROM documents WHERE id = @id",
                new { id = d.Id });

            row.Should().NotBeNull($"[{context}] document {d.Id} must exist");
            row!.Status.Should().Be((short)d.Status);

            if (d.Status == DocumentStatus.Posted)
                row.PostedAtUtc.Should().NotBeNull();
            else
                row.PostedAtUtc.Should().BeNull();
        }

        // 2) Relationships must match shadow (and must not reference missing docs).
        var dbRelationshipIds = (await conn.QueryAsync<Guid>("SELECT relationship_id FROM document_relationships"))
            .ToHashSet();

        var shadowIds = state.Relationships
            .Select(r => DeterministicGuid.Create($"DocumentRelationship|{r.From:D}|{r.CodeNorm}|{r.To:D}"))
            .ToHashSet();

        dbRelationshipIds.Should().BeEquivalentTo(shadowIds, $"[{context}] relationships must match shadow");

        // 3) There must be no relationships referencing documents that do not exist (cascade delete invariant).
        var orphanCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT count(*)
            FROM document_relationships r
            LEFT JOIN documents d1 ON d1.id = r.from_document_id
            LEFT JOIN documents d2 ON d2.id = r.to_document_id
            WHERE d1.id IS NULL OR d2.id IS NULL;
            """);

        orphanCount.Should().Be(0, $"[{context}] relationships must not reference missing documents");
    }

    private static async Task<int> CountAuditEventsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM platform_audit_events");
    }

    private static async Task<int> CountUsersForSubjectAsync(string cs, string subject)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM platform_users WHERE auth_subject = @subject",
            new { subject });
    }

    private static T Pick<T>(IReadOnlyCollection<T> items, Random rng)
    {
        if (items.Count == 0)
            throw new XunitException("Cannot pick from an empty collection.");

        var index = rng.Next(items.Count);

        // Fast path for indexable collections.
        if (items is IList<T> list)
            return list[index];

        // Fallback for non-indexable collections (e.g., Dictionary.ValueCollection, HashSet).
        return items.ElementAt(index);
    }

    private enum ChaosOp
    {
        CreateDraft,
        UpdateDraft,
        DeleteDraft,
        MarkForDeletion,
        UnmarkForDeletion,
        CreateRelationship,
        DeleteRelationship,
        Post,
        Unpost,
        Repost
    }

    private sealed class ShadowState
    {
        public Dictionary<Guid, ShadowDoc> Documents { get; } = new();
        public HashSet<ShadowRelationship> Relationships { get; } = new();
        public int NextNumber { get; set; } = 1;
    }

    private sealed record ShadowDoc(
        Guid Id,
        string Number,
        DateTime DateUtc,
        DocumentStatus Status,
        bool HasPostLog,
        bool HasUnpostLog,
        bool HasRepostLog);

    private sealed record ShadowRelationship(Guid From, Guid To, string CodeNorm);

    private sealed record DocRow(Guid Id, short Status, DateTime? PostedAtUtc);
}
