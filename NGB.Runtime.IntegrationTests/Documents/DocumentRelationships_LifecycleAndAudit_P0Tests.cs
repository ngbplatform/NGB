using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_LifecycleAndAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_Delete_AreIdempotent_AndAuditNoOpsAreNotLogged()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var (fromId, toId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

        // Act 1: create
        (await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        // Act 2: create again (no-op)
        (await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeFalse();

        // Assert: only one create event
        var createdEvents = await audit.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.DocumentRelationship,
                EntityId: relId,
                ActionCode: NGB.Runtime.AuditLog.AuditActionCodes.DocumentRelationshipCreate,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        createdEvents.Should().HaveCount(1);

        // Assert: list outgoing / incoming
        var outgoing = await svc.ListOutgoingAsync(fromId, CancellationToken.None);
        outgoing.Should().ContainSingle(x => x.ToDocumentId == toId && x.RelationshipCodeNorm == codeNorm);

        var incoming = await svc.ListIncomingAsync(toId, CancellationToken.None);
        incoming.Should().ContainSingle(x => x.FromDocumentId == fromId && x.RelationshipCodeNorm == codeNorm);

        // Act 3: delete
        (await svc.DeleteAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        // Act 4: delete again (no-op)
        (await svc.DeleteAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeFalse();

        // Assert: only one delete event
        var deletedEvents = await audit.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.DocumentRelationship,
                EntityId: relId,
                ActionCode: NGB.Runtime.AuditLog.AuditActionCodes.DocumentRelationshipDelete,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        deletedEvents.Should().HaveCount(1);

        // Assert: lists are empty
        (await svc.ListOutgoingAsync(fromId, CancellationToken.None)).Should().BeEmpty();
        (await svc.ListIncomingAsync(toId, CancellationToken.None)).Should().BeEmpty();
    }

    private static async Task<(Guid FromId, Guid ToId)> CreateTwoDraftDocsAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (fromId, toId);
    }
}
