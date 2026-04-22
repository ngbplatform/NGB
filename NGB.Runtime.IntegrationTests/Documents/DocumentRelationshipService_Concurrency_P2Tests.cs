using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_Concurrency_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_ConcurrentSameEdge_OneRowOnly_NoDeadlocks_AndSingleAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var setupScope = host.Services.CreateAsyncScope();
        var (fromId, toId) = await CreateTwoDraftDocsAsync(setupScope.ServiceProvider);

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        const int taskCount = 32;

        var tasks = Enumerable.Range(0, taskCount).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token)).ToArray();

        start.SetResult();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex) when (FindPostgresException(ex)?.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            throw new Xunit.Sdk.XunitException(
                "Deadlock detected while concurrently creating the same document relationship edge.");
        }

        var results = tasks.Select(t => t.Result).ToArray();
        results.Count(x => x).Should().Be(1);
        results.Count(x => !x).Should().Be(taskCount - 1);

        await using var verifyScope = host.Services.CreateAsyncScope();
        var svcVerify = verifyScope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        var outgoing = await svcVerify.ListOutgoingAsync(fromId, CancellationToken.None);
        outgoing.Should().ContainSingle(x => x.ToDocumentId == toId && x.RelationshipCodeNorm == codeNorm);

        var audit = verifyScope.ServiceProvider.GetRequiredService<IAuditEventReader>();
        var events = await audit.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.DocumentRelationship,
                EntityId: relId,
                ActionCode: NGB.Runtime.AuditLog.AuditActionCodes.DocumentRelationshipCreate,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        events.Should().HaveCount(1, "concurrent idempotent creates must not produce multiple audit events");
    }

    [Fact]
    public async Task CreateAsync_ConcurrentBidirectionalCrossCreate_NoDeadlocks_AndExactlyTwoDirectedEdges()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var setupScope = host.Services.CreateAsyncScope();
        var (a, b) = await CreateTwoDraftDocsAsync(setupScope.ServiceProvider);

        const string code = "related_to";
        var codeNorm = code.ToLowerInvariant();

        var relIdAB = DeterministicGuid.Create($"DocumentRelationship|{a:D}|{codeNorm}|{b:D}");
        var relIdBA = DeterministicGuid.Create($"DocumentRelationship|{b:D}|{codeNorm}|{a:D}");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var t1 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(a, b, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        var t2 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(b, a, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        start.SetResult();

        var ex1 = await Record.ExceptionAsync(() => t1);
        var ex2 = await Record.ExceptionAsync(() => t2);

        if (FindPostgresException(ex1)?.SqlState == PostgresErrorCodes.DeadlockDetected ||
            FindPostgresException(ex2)?.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            throw new Xunit.Sdk.XunitException("Deadlock detected in bidirectional cross-create race.");
        }

        // At least one should actually create (the other may become a no-op).
        var r1 = ex1 is null ? await t1 : false;
        var r2 = ex2 is null ? await t2 : false;
        (r1 || r2).Should().BeTrue();

        await using var verifyScope = host.Services.CreateAsyncScope();
        var svcVerify = verifyScope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        (await svcVerify.ListOutgoingAsync(a, CancellationToken.None))
            .Should().ContainSingle(x => x.ToDocumentId == b && x.RelationshipCodeNorm == codeNorm);

        (await svcVerify.ListOutgoingAsync(b, CancellationToken.None))
            .Should().ContainSingle(x => x.ToDocumentId == a && x.RelationshipCodeNorm == codeNorm);

        var audit = verifyScope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var createdAB = await audit.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.DocumentRelationship,
                EntityId: relIdAB,
                ActionCode: NGB.Runtime.AuditLog.AuditActionCodes.DocumentRelationshipCreate,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        var createdBA = await audit.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.DocumentRelationship,
                EntityId: relIdBA,
                ActionCode: NGB.Runtime.AuditLog.AuditActionCodes.DocumentRelationshipCreate,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        createdAB.Should().HaveCount(1);
        createdBA.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentManyToOneCardinalityRace_NoDeadlocks_AndMaxOneOutgoingIsEnforced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var setupScope = host.Services.CreateAsyncScope();
        var ids = await CreateDraftDocsAsync(setupScope.ServiceProvider, count: 3);
        var from = ids[0];
        var to1 = ids[1];
        var to2 = ids[2];

        const string code = "reversal_of";
        var codeNorm = code.ToLowerInvariant();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var t1 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(from, to1, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        var t2 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(from, to2, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        start.SetResult();

        var ex1 = await Record.ExceptionAsync(() => t1);
        var ex2 = await Record.ExceptionAsync(() => t2);

        if (FindPostgresException(ex1)?.SqlState == PostgresErrorCodes.DeadlockDetected ||
            FindPostgresException(ex2)?.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            throw new Xunit.Sdk.XunitException("Deadlock detected in ManyToOne cardinality race.");
        }

        // One should succeed; the other should fail due to cardinality.
        (ex1 is null || ex2 is null).Should().BeTrue();

        if (ex1 is not null)
        {
            var pg = FindPostgresException(ex1);
            (ex1 is NgbException || (pg is not null && pg.SqlState == PostgresErrorCodes.UniqueViolation))
                .Should().BeTrue("the losing side must fail with a cardinality violation or equivalent guard");
        }

        if (ex2 is not null)
        {
            var pg = FindPostgresException(ex2);
            (ex2 is NgbException || (pg is not null && pg.SqlState == PostgresErrorCodes.UniqueViolation))
                .Should().BeTrue("the losing side must fail with a cardinality violation or equivalent guard");
        }

        await using var verifyScope = host.Services.CreateAsyncScope();
        var svcVerify = verifyScope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var outgoing = (await svcVerify.ListOutgoingAsync(from, CancellationToken.None))
            .Where(x => x.RelationshipCodeNorm == codeNorm)
            .ToArray();

        outgoing.Should().HaveCount(1, "ManyToOne relationship must allow at most one outgoing edge per from");
    }

    [Fact]
    public async Task CreateAsync_ConcurrentOneToOneIncomingCardinalityRace_NoDeadlocks_AndMaxOneIncomingIsEnforced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var setupScope = host.Services.CreateAsyncScope();
        var ids = await CreateDraftDocsAsync(setupScope.ServiceProvider, count: 3);
        var from1 = ids[0];
        var from2 = ids[1];
        var to = ids[2];

        const string code = "supersedes";
        var codeNorm = code.ToLowerInvariant();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var t1 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(from1, to, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        var t2 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            return await svc.CreateAsync(from2, to, code, manageTransaction: true, ct: cts.Token);
        }, cts.Token);

        start.SetResult();

        var ex1 = await Record.ExceptionAsync(() => t1);
        var ex2 = await Record.ExceptionAsync(() => t2);

        if (FindPostgresException(ex1)?.SqlState == PostgresErrorCodes.DeadlockDetected ||
            FindPostgresException(ex2)?.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            throw new Xunit.Sdk.XunitException("Deadlock detected in OneToOne incoming cardinality race.");
        }

        // One should succeed; the other should fail due to cardinality.
        (ex1 is null || ex2 is null).Should().BeTrue();

        if (ex1 is not null)
        {
            var pg = FindPostgresException(ex1);
            (ex1 is NgbException || (pg is not null && pg.SqlState == PostgresErrorCodes.UniqueViolation))
                .Should().BeTrue("the losing side must fail with a cardinality violation or equivalent guard");
        }

        if (ex2 is not null)
        {
            var pg = FindPostgresException(ex2);
            (ex2 is NgbException || (pg is not null && pg.SqlState == PostgresErrorCodes.UniqueViolation))
                .Should().BeTrue("the losing side must fail with a cardinality violation or equivalent guard");
        }

        await using var verifyScope = host.Services.CreateAsyncScope();
        var svcVerify = verifyScope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var incoming = (await svcVerify.ListIncomingAsync(to, CancellationToken.None))
            .Where(x => x.RelationshipCodeNorm == codeNorm)
            .ToArray();

        incoming.Should().HaveCount(1, "OneToOne relationship must allow at most one incoming edge per to");
    }

    private static PostgresException? FindPostgresException(Exception? ex)
    {
        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.Flatten().InnerExceptions)
            {
                var found = FindPostgresException(inner);
                if (found is not null)
                    return found;
            }

            return null;
        }

        while (ex is not null)
        {
            if (ex is PostgresException pg)
                return pg;

            ex = ex.InnerException;
        }

        return null;
    }

    private static async Task<(Guid A, Guid B)> CreateTwoDraftDocsAsync(IServiceProvider sp)
    {
        var ids = await CreateDraftDocsAsync(sp, count: 2);
        return (ids[0], ids[1]);
    }

    private static async Task<Guid[]> CreateDraftDocsAsync(IServiceProvider sp, int count)
    {
        if (count <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");

        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            for (var i = 0; i < ids.Length; i++)
            {
                await repo.CreateAsync(new DocumentRecord
                {
                    Id = ids[i],
                    TypeCode = i == 0 ? "it_alpha" : "it_beta",
                    Number = $"IT-{i + 1:0000}",
                    DateUtc = nowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }
        }, CancellationToken.None);

        return ids;
    }
}
