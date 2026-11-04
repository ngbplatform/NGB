using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Security;
using NGB.Persistence.Documents.Actions;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;
using ContractStatus = NGB.Contracts.Metadata.DocumentStatus;
using Harness = NGB.Runtime.Tests.Documents.Actions.DocumentActionDispatcherCoverageTests.Harness;

namespace NGB.Runtime.Tests.Documents.Actions;

[Collection(Observability.TelemetrySerialCollection.Name)]
public sealed class DocumentActionReplayAuthorizationTests
{
    private const string SourceType = "test.source";
    private const string TargetType = "test.target";

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public async Task Replay_requires_current_view_action_and_active_authenticated_access(
        bool canView,
        bool canPost,
        bool isActive,
        bool isAuthenticated)
    {
        var harness = Completed();
        var grants = new List<NgbPermissionKey>();
        if (canView)
            grants.Add(Permission(SourceType, NgbPermissionActions.View));
        if (canPost)
            grants.Add(Permission(SourceType, NgbPermissionActions.Post));
        var current = new PermissionSnapshot(
            harness.UserId, "current-actor", isAuthenticated, isActive, false, 2, grants);
        harness.Permissions
            .Setup(provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        var replay = () => harness.ExecuteAsync();

        await replay.Should().ThrowAsync<DocumentActionForbiddenException>();
        harness.Permissions.Verify(
            provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()), Times.Once);
        harness.Permissions.Verify(
            provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()), Times.Never);
        harness.Posting.VerifyNoOtherCalls();
        harness.Uow.Verify(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(DocumentStatus.Posted, true)]
    [InlineData(DocumentStatus.Posted, false)]
    [InlineData(DocumentStatus.MarkedForDeletion, false)]
    public async Task Authorized_replay_preserves_the_original_result_after_status_and_version_change(
        DocumentStatus status,
        bool bootstrapAdmin)
    {
        var harness = Completed();
        harness.Source = CurrentDocument(harness, status, version: 8);
        var current = new PermissionSnapshot(
            bootstrapAdmin ? null : harness.UserId,
            "current-administrator",
            true,
            true,
            bootstrapAdmin,
            2,
            [Permission(SourceType, NgbPermissionActions.View), Permission(SourceType, NgbPermissionActions.Post)]);
        harness.Permissions
            .Setup(provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        var result = await harness.ExecuteAsync(expectedVersion: 1);

        result.ExecutionId.Should().Be(harness.ExecutionId);
        result.DocumentVersion.Should().Be(1);
        result.Document.Display.Should().Be("Saved response");
        harness.Source.Version.Should().Be(8);
        harness.Posting.VerifyNoOtherCalls();
        harness.Outbox.VerifyNoOtherCalls();
        harness.Documents.Verify(
            repository => repository.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Documents.Verify(
            repository => repository.IncrementVersionAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Executions.Verify(
            repository => repository.MarkCompletedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replay_evaluates_custom_authorization_with_current_facts_without_execution_availability(bool authorized)
    {
        var action = new DocumentActionCode("test.evaluated-command");
        var harness = Completed(action);
        harness.Source = CurrentDocument(harness, DocumentStatus.Posted, version: 7);
        harness.Enricher.Ready = authorized;
        harness.Availability.RejectEvaluation = true;

        var replay = () => harness.ExecuteAsync(action, expectedVersion: 1);

        if (authorized)
            (await replay()).ExecutionId.Should().Be(harness.ExecutionId);
        else
            await replay.Should().ThrowAsync<DocumentActionForbiddenException>();

        harness.Enricher.CallCount.Should().Be(1);
        harness.Enricher.LastRequest!.Document.Version.Should().Be(7);
        harness.Handler.CallCount.Should().Be(0);
        harness.Posting.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("test.command", true)]
    [InlineData("test.command", false)]
    [InlineData("test.derive", true)]
    [InlineData("test.derive", false)]
    public async Task Replay_requires_access_to_the_created_document(string actionCode, bool canViewCreated)
    {
        var action = new DocumentActionCode(actionCode);
        var harness = Completed(action, includeCreated: true);
        var grants = new List<NgbPermissionKey>
        {
            Permission(SourceType, NgbPermissionActions.View),
            Permission(TargetType, NgbPermissionActions.Create)
        };
        if (canViewCreated)
            grants.Add(Permission(TargetType, NgbPermissionActions.View));
        harness.Permissions
            .Setup(provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(harness.UserId, "actor", true, true, false, 2, grants));

        var replay = () => harness.ExecuteAsync(action);

        if (canViewCreated)
            (await replay()).CreatedDocument!.Id.Should().Be(harness.Created.Id);
        else
            await replay.Should().ThrowAsync<DocumentActionForbiddenException>();

        harness.Derivations.VerifyNoOtherCalls();
        harness.Handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_refuses_results_whose_source_or_created_document_no_longer_exists(bool createdMissing)
    {
        var harness = Completed(includeCreated: createdMissing);
        var missingId = createdMissing ? harness.Created.Id : harness.Source.Id;
        harness.Documents
            .Setup(repository => repository.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var replay = () => harness.ExecuteAsync();

        await replay.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task Replay_checks_the_current_document_type()
    {
        var harness = Completed();
        harness.Source = new DocumentRecord
        {
            Id = harness.Source.Id,
            TypeCode = TargetType,
            DateUtc = harness.Source.DateUtc,
            Status = DocumentStatus.Posted
        };

        var replay = () => harness.ExecuteAsync();

        await replay.Should().ThrowAsync<DocumentTypeMismatchException>();
    }

    [Fact]
    public async Task Custom_replay_authorization_requires_the_current_typed_document()
    {
        var action = new DocumentActionCode("test.evaluated-command");
        var harness = Completed(action, returnNullDocumentDto: true);

        var replay = () => harness.ExecuteAsync(action);

        await replay.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    private static Harness Completed(
        DocumentActionCode? action = null,
        bool includeCreated = false,
        bool returnNullDocumentDto = false)
    {
        var harness = new Harness(
            beginStatus: DocumentActionExecutionBeginStatus.Completed,
            initialStatus: DocumentStatus.Posted,
            returnNullDocumentDto: returnNullDocumentDto);
        var stored = new ExecuteDocumentActionResultDto(
            harness.ExecutionId,
            (action ?? StandardDocumentActionCodes.Post).Value,
            SavedDocument(harness.Source.Id),
            1,
            [],
            WorkCenterMayChange: true,
            CreatedDocument: includeCreated ? SavedDocument(harness.Created.Id) : null);
        harness.StoredResultJson = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return harness;
    }

    private static DocumentDto SavedDocument(Guid id)
        => new(id, "Saved response", new RecordPayload(), ContractStatus.Posted, false);

    private static DocumentRecord CurrentDocument(Harness harness, DocumentStatus status, long version)
        => new()
        {
            Id = harness.Source.Id,
            TypeCode = SourceType,
            DateUtc = harness.Source.DateUtc,
            Status = status,
            Version = version
        };

    private static NgbPermissionKey Permission(string type, string action)
        => new(NgbResourceKinds.Document, type, action);
}
