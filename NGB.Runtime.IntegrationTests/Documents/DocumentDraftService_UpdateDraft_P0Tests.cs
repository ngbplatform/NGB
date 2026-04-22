using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_UpdateDraft_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task UpdateDraftAsync_WhenNumberAndDateChange_UpdatesDocument_AndWritesAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var originalDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var newDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        DocumentRecord original;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-0001", dateUtc: originalDate);
            original = await documents.GetAsync(documentId) ?? throw new XunitException("Draft not found.");

            var didWork = await drafts.UpdateDraftAsync(documentId, number: "D-0002", dateUtc: newDate);
            didWork.Should().BeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var updated = await documents.GetAsync(documentId) ?? throw new XunitException("Draft not found.");
            updated.Number.Should().Be("D-0002");
            updated.DateUtc.Should().Be(newDate);
            updated.UpdatedAtUtc.Should().BeAfter(original.UpdatedAtUtc);

            var events = await audit.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUpdateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.Changes.Select(c => c.FieldPath)
                .Should()
                .Contain(new[] { "number", "date_utc", "updated_at_utc" });

            ev.Changes.Single(c => c.FieldPath == "number").OldValueJson.Should().Contain("D-0001");
            ev.Changes.Single(c => c.FieldPath == "number").NewValueJson.Should().Contain("D-0002");
        }
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenNoChanges_ReturnsFalse_AndDoesNotWriteAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var dateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-0100", dateUtc: dateUtc);

            var didWork = await drafts.UpdateDraftAsync(documentId, number: "D-0100", dateUtc: dateUtc);
            didWork.Should().BeFalse();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await audit.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUpdateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenMarkedForDeletion_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var dateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-MD", dateUtc: dateUtc);
            await posting.MarkForDeletionAsync(documentId);

            var act = () => drafts.UpdateDraftAsync(documentId, number: "D-MD-2", dateUtc: null);

            var ex = await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
            ex.Which.ErrorCode.Should().Be(DocumentMarkedForDeletionException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("DocumentDraft.UpdateDraft");
        }
    }
}
