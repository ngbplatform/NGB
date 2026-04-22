using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.PostingResolvers;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_PostingActionResolverFailures_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WithoutPostingAction_Throws_WhenNoPostingHandlerConfigured_StatusStaysDraft_NoPostingLog_NoAudit_P0()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();

        var dateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, typeCode: "foo", dateUtc: dateUtc, number: "X-1");

        // Act
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await posting.PostAsync(docId, CancellationToken.None);
        };

        // Assert
        var ex = await act.Should().ThrowAsync<DocumentPostingHandlerNotConfiguredException>();

        ex.Which.ErrorCode.Should().Be(DocumentPostingHandlerNotConfiguredException.Code);
        ex.Which.DocumentId.Should().Be(docId);
        ex.Which.TypeCode.Should().Be("foo");

        await AssertDocumentIsStillDraft_AndNoPostSideEffectsAsync(host, window, docId);
    }

    [Fact]
    public async Task PostAsync_WithoutPostingAction_Throws_WhenPostingHandlerTypeCodeMismatch_StatusStaysDraft_NoPostingLog_NoAudit_P0()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost(services =>
        {
            services.AddSingleton<IDefinitionsContributor, TypeCodeMismatchContributor>();
            services.AddScoped<TypeCodeMismatchContributor.MismatchPostingHandler>();
        });

        var dateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, typeCode: TypeCodeMismatchContributor.TypeCode, dateUtc: dateUtc, number: "X-2");

        // Act
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await posting.PostAsync(docId, CancellationToken.None);
        };

        // Assert
        var ex = await act.Should().ThrowAsync<DocumentPostingHandlerMisconfiguredException>();
        ex.Which.ErrorCode.Should().Be(DocumentPostingHandlerMisconfiguredException.Code);
        ex.Which.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("accounting");
        ex.Which.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(TypeCodeMismatchContributor.TypeCode);
        ex.Which.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("TypeCode does not match");

        await AssertDocumentIsStillDraft_AndNoPostSideEffectsAsync(host, window, docId);
    }

    private IHost CreateHost(Action<IServiceCollection>? configure = null)
        => IntegrationHostFactory.Create(Fixture.ConnectionString, configure);

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: typeCode,
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task AssertDocumentIsStillDraft_AndNoPostSideEffectsAsync(IHost host, PostingLogTestWindow window, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docs = sp.GetRequiredService<IDocumentRepository>();
        var doc = await docs.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();
        doc.MarkedForDeletionAtUtc.Should().BeNull();

        var logs = await sp.GetRequiredService<IPostingStateReader>().GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            DocumentId = docId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        logs.Records.Should().BeEmpty("PostAsync failure must rollback posting_log begin row");

        var audit = sp.GetRequiredService<IAuditEventReader>();
        var postEvents = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document,
            EntityId: docId,
            ActionCode: AuditActionCodes.DocumentPost,
            FromUtc: window.FromUtc,
            ToUtc: window.ToUtc,
            Limit: 50), CancellationToken.None);

        postEvents.Should().BeEmpty("failed PostAsync must not emit audit event");
    }

    private sealed class TypeCodeMismatchContributor : IDefinitionsContributor
    {
        public const string TypeCode = "it_doc_post_mismatch";

        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("IT Doc Post Mismatch"),
                    Version: new DocumentMetadataVersion(1, "it-tests")))
                .PostingHandler<MismatchPostingHandler>());
        }

        public sealed class MismatchPostingHandler : IDocumentPostingHandler
        {
            public string TypeCode => "some_other_type";

            public Task BuildEntriesAsync(DocumentRecord document, NGB.Accounting.Posting.IAccountingPostingContext ctx, CancellationToken ct)
                => Task.CompletedTask;
        }
    }
}
