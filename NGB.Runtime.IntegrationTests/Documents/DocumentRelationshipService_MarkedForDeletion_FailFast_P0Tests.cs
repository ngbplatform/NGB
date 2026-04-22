using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_MarkedForDeletion_FailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_WhenFromDocumentMarkedForDeletion_Throws_AndNoRelationshipOrAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await CreateDocsAsync(host,
            new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it.doc.from",
                Number = "FROM-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.MarkedForDeletion,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = NowUtc
            },
            new DocumentRecord
            {
                Id = toId,
                TypeCode = "it.doc.to",
                Number = "TO-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            });

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");
        var baseline = await CaptureBaselineAsync(relationshipId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            Func<Task> act = () => svc.CreateAsync(
                fromDocumentId: fromId,
                toDocumentId: toId,
                relationshipCode: "based_on",
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.AssertNgbError(DocumentRelationshipValidationException.Code, "reason", "relationshipCode", "fromDocumentId", "toDocumentId");
            ex.Which.AssertReason("from_document_must_be_draft");
        }

        await AssertNoSideEffectsAsync(baseline);
    }

    [Fact]
    public async Task DeleteAsync_WhenFromDocumentMarkedForDeletion_Throws_AndNoAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await CreateDocsAsync(host,
            new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it.doc.from.del",
                Number = "FROM-DEL-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.MarkedForDeletion,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = NowUtc
            },
            new DocumentRecord
            {
                Id = toId,
                TypeCode = "it.doc.to.del",
                Number = "TO-DEL-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            });

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");
        var baseline = await CaptureBaselineAsync(relationshipId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            Func<Task> act = () => svc.DeleteAsync(
                fromDocumentId: fromId,
                toDocumentId: toId,
                relationshipCode: "based_on",
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.AssertNgbError(DocumentRelationshipValidationException.Code, "reason", "relationshipCode", "fromDocumentId", "toDocumentId");
            ex.Which.AssertReason("from_document_must_be_draft");
        }

        await AssertNoSideEffectsAsync(baseline);
    }

    [Fact]
    public async Task CreateAsync_WhenBidirectional_AndToDocumentMarkedForDeletion_Throws_AndNoRelationshipOrAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await CreateDocsAsync(host,
            new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it.doc.from.bi",
                Number = "FROM-BI-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            },
            new DocumentRecord
            {
                Id = toId,
                TypeCode = "it.doc.to.bi",
                Number = "TO-BI-1",
                DateUtc = NowUtc,
                Status = DocumentStatus.MarkedForDeletion,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = NowUtc
            });

        // related_to is bidirectional; service would attempt to create both directions (but must fail before any write).
        var rel1 = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|related_to|{toId:D}");
        var rel2 = DeterministicGuid.Create($"DocumentRelationship|{toId:D}|related_to|{fromId:D}");

        var baseline1 = await CaptureBaselineAsync(rel1);
        var baseline2 = await CaptureBaselineAsync(rel2);

        // baselines must match (no rows), but we keep both IDs to assert no partial insert happened.
        baseline1.AuditEvents.Should().Be(baseline2.AuditEvents);
        baseline1.AuditChanges.Should().Be(baseline2.AuditChanges);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            Func<Task> act = () => svc.CreateAsync(
                fromDocumentId: fromId,
                toDocumentId: toId,
                relationshipCode: "related_to",
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.AssertNgbError(DocumentRelationshipValidationException.Code, "reason", "relationshipCode", "fromDocumentId", "toDocumentId");
            ex.Which.AssertReason("bidirectional_requires_both_draft");
        }

        // Must not create either direction and must not write audit.
        await AssertNoSideEffectsAsync(baseline1);
        await AssertNoRelationshipRowAsync(rel2, expected: 0);
    }

    private sealed record Baseline(int AuditEvents, int AuditChanges, int RelationshipRows, Guid RelationshipId);

    private static async Task CreateDocsAsync(IHost host, DocumentRecord a, DocumentRecord b)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(a, ct);
            await repo.CreateAsync(b, ct);
        }, CancellationToken.None);
    }

    private async Task<Baseline> CaptureBaselineAsync(Guid relationshipId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changesCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var relCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @id;",
            new { id = relationshipId });

        return new Baseline(eventsCount, changesCount, relCount, relationshipId);
    }

    private async Task AssertNoSideEffectsAsync(Baseline baseline)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEvents);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChanges);

        await AssertNoRelationshipRowAsync(baseline.RelationshipId, baseline.RelationshipRows);
    }

    private async Task AssertNoRelationshipRowAsync(Guid relationshipId, int expected)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @id;",
                new { id = relationshipId }))
            .Should().Be(expected);
    }
}
