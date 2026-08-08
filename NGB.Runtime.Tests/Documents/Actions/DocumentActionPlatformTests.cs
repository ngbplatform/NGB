using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.WorkCenter;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Definitions.WorkCenter;
using NGB.Metadata.Documents.Actions;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents.Actions;
using NGB.Runtime.Security;
using NGB.Runtime.WorkCenter;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Actions;

public sealed class DocumentActionPlatformTests
{
    [Theory]
    [InlineData("ngb.post")]
    [InlineData("crm.create_qualification")]
    [InlineData("vertical:action-name")]
    public void ActionCode_accepts_canonical_extensible_values(string value)
    {
        var code = new DocumentActionCode(value);

        code.Value.Should().Be(value);
        code.ToString().Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" NGB.Post ")]
    [InlineData("contains spaces")]
    [InlineData("ngb/action")]
    public void ActionCode_rejects_non_canonical_values(string value)
    {
        var act = () => new DocumentActionCode(value);

        act.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Runtime_registers_a_deterministic_standard_action_set_for_every_document()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<DocumentActionRegistry>();
        var actions = registry.GetForDocumentType(AccountingDocumentTypeCodes.GeneralJournalEntry);

        actions.Select(static x => x.Metadata.Code.Value).Should().Contain(
        [
            StandardDocumentActionCodes.MarkForDeletion.Value,
            StandardDocumentActionCodes.UnmarkForDeletion.Value,
            StandardDocumentActionCodes.ViewEffects.Value,
            StandardDocumentActionCodes.ViewFlow.Value,
            StandardDocumentActionCodes.ViewAudit.Value,
            StandardDocumentActionCodes.Print.Value
        ]);
        actions.Select(static x => x.Metadata.Order).Should().BeInAscendingOrder();
        actions.Select(static x => x.Metadata.Code.Value).Should().OnlyHaveUniqueItems();
        registry.Get(AccountingDocumentTypeCodes.GeneralJournalEntry, StandardDocumentActionCodes.Print)
            .Metadata.ExecutionKind.Should().Be(DocumentActionExecutionKind.View);
        registry.GetForDocumentType("missing.document").Should().BeEmpty();
        var missing = () => registry.Get("missing.document", StandardDocumentActionCodes.Post);
        missing.Should().Throw<DocumentActionNotFoundException>();
    }

    [Theory]
    [InlineData(NGB.Core.Documents.DocumentStatus.Draft, "post", true)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Draft, "unpost", false)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Draft, "repost", false)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Draft, "mark_for_deletion", true)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Draft, "unmark_for_deletion", false)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Posted, "post", false)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Posted, "unpost", true)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Posted, "repost", true)]
    [InlineData(NGB.Core.Documents.DocumentStatus.Posted, "mark_for_deletion", false)]
    [InlineData(NGB.Core.Documents.DocumentStatus.MarkedForDeletion, "unmark_for_deletion", true)]
    public async Task Evaluator_applies_standard_lifecycle_availability(
        NGB.Core.Documents.DocumentStatus status,
        string actionCode,
        bool expectedAllowed)
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<DocumentActionEvaluator>();
        var id = Guid.NewGuid();
        var document = new DocumentRecord
        {
            Id = id,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = DateTime.UtcNow,
            Status = status,
            Version = 1
        };
        var dto = new DocumentDto(
            id,
            "GJE",
            new RecordPayload(),
            status switch
            {
                NGB.Core.Documents.DocumentStatus.Draft => NGB.Contracts.Metadata.DocumentStatus.Draft,
                NGB.Core.Documents.DocumentStatus.Posted => NGB.Contracts.Metadata.DocumentStatus.Posted,
                _ => NGB.Contracts.Metadata.DocumentStatus.MarkedForDeletion
            },
            status == NGB.Core.Documents.DocumentStatus.MarkedForDeletion);
        var snapshot = new PermissionSnapshot(
            Guid.NewGuid(),
            "subject",
            true,
            true,
            true,
            1,
            []);

        var facts = await evaluator.LoadFactsAsync(document, dto, snapshot, CancellationToken.None);
        var code = new DocumentActionCode(actionCode);
        var evaluated = await evaluator.EvaluateOneAsync(
            new DocumentActionDefinition(
                AccountingDocumentTypeCodes.GeneralJournalEntry,
                new DocumentActionMetadata(
                    code,
                    new DocumentActionPresentation("Action"),
                    DocumentActionKind.Secondary,
                    DocumentActionExecutionKind.Command,
                    1)),
            document,
            dto,
            snapshot,
            facts,
            CancellationToken.None);

        facts.Should().BeEmpty();
        evaluated.Dto.IsAllowed.Should().Be(expectedAllowed);
        if (expectedAllowed)
            evaluated.Dto.DisabledReasons.Should().BeEmpty();
        else
            evaluated.Dto.DisabledReasons.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Evaluator_hides_all_actions_without_permissions_and_maps_view_targets()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<DocumentActionEvaluator>();
        var id = Guid.NewGuid();
        var document = new DocumentRecord
        {
            Id = id,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = DateTime.UtcNow,
            Status = NGB.Core.Documents.DocumentStatus.Draft,
            Version = 1
        };
        var dto = new DocumentDto(
            id,
            "GJE",
            new RecordPayload(),
            NGB.Contracts.Metadata.DocumentStatus.Draft,
            false);
        var denied = new PermissionSnapshot(Guid.NewGuid(), "subject", true, true, false, 1, []);

        (await evaluator.EvaluateAllAsync(
            document,
            dto,
            denied,
            new Dictionary<string, object?>(),
            CancellationToken.None)).Should().BeEmpty();

        var allowed = new PermissionSnapshot(Guid.NewGuid(), "subject", true, true, true, 1, []);
        var actions = await evaluator.EvaluateAllAsync(
            document,
            dto,
            allowed,
            new Dictionary<string, object?>(),
            CancellationToken.None);
        var flow = actions.Single(action => action.Dto.Code == StandardDocumentActionCodes.ViewFlow.Value);
        flow.Dto.Target!.Parameters["documentId"].Should().Be(id.ToString());
        flow.Dto.Confirmation.Should().BeNull();
        actions.Single(action => action.Dto.Code == StandardDocumentActionCodes.MarkForDeletion.Value)
            .Dto.Confirmation.Should().NotBeNull();
    }

    [Theory]
    [InlineData("unknown_document")]
    [InlineData("missing_handler")]
    [InlineData("view_with_handler")]
    [InlineData("wrong_handler")]
    [InlineData("wrong_availability")]
    [InlineData("wrong_authorization")]
    public void Registry_rejects_invalid_vertical_extension_contracts(string scenario)
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        using var provider = services.BuildServiceProvider();
        var definitions = provider.GetRequiredService<DefinitionsRegistry>();
        var documentType = scenario == "unknown_document"
            ? "missing.document"
            : AccountingDocumentTypeCodes.GeneralJournalEntry;
        var contributor = new InlineActionContributor(builder =>
        {
            var execution = scenario == "view_with_handler"
                ? DocumentActionExecutionKind.View
                : DocumentActionExecutionKind.Command;
            builder.Add(
                documentType,
                new DocumentActionMetadata(
                    new DocumentActionCode($"test.{scenario}"),
                    new DocumentActionPresentation("Test"),
                    DocumentActionKind.Secondary,
                    execution,
                    700,
                    Target: execution == DocumentActionExecutionKind.View
                        ? new DocumentActionTargetMetadata(
                            "document.editor",
                            new Dictionary<string, string?>())
                        : null),
                handlerType: scenario switch
                {
                    "missing_handler" => null,
                    "wrong_handler" => typeof(string),
                    _ => typeof(TestActionHandler)
                },
                availabilityEvaluatorType: scenario == "wrong_availability" ? typeof(string) : null,
                authorizationEvaluatorType: scenario == "wrong_authorization" ? typeof(string) : null);
        });

        var action = () => new DocumentActionRegistry(definitions, [contributor]);

        action.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Work_Center_preference_registry_applies_platform_defaults_and_validates_extensions()
    {
        var registry = new WorkCenterPreferenceDefinitionRegistry(
        [
            new TestNotificationSource(
                new WorkCenterPreferenceDefinition(
                    "test.document_ready",
                    WorkCenterPreferenceKind.Notification,
                    "Document ready",
                    "Documents",
                    DefaultEnabled: true,
                    UserCanDisable: true,
                    DefaultSeverity: NotificationSeverity.Success,
                    SupportedChannels: new HashSet<NotificationChannel> { NotificationChannel.InApp },
                    Retention: TimeSpan.FromDays(30)))
        ]);

        registry.Get("TEST.DOCUMENT_READY").DefaultEnabled.Should().BeTrue();

        var act = () => new WorkCenterPreferenceDefinitionRegistry(
        [
            new TestNotificationSource(
                new WorkCenterPreferenceDefinition(
                    "test.required",
                    WorkCenterPreferenceKind.Notification,
                    "Required",
                    "Security",
                    DefaultEnabled: true,
                    UserCanDisable: true,
                    DefaultSeverity: NotificationSeverity.Warning,
                    SupportedChannels: new HashSet<NotificationChannel> { NotificationChannel.InApp },
                    Retention: null,
                    IsMandatory: true))
        ]);
        act.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Work_Center_preference_registry_orders_definitions_and_resolves_case_insensitively()
    {
        var registry = new WorkCenterPreferenceDefinitionRegistry(
        [
            new TestNotificationSource(
                Notification("z.last", "Zulu", "Z category"),
                Notification("a.second", "Beta", "A category"),
                Notification("a.first", "Alpha", "A category"))
        ]);

        registry.All.Select(static definition => definition.Code)
            .Should().Equal(
                "a.first",
                "a.second",
                "z.last");
        registry.Get("A.FIRST").DisplayName.Should().Be("Alpha");

        var unknown = () => registry.Get("missing.notification");
        unknown.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");
    }

    [Theory]
    [MemberData(nameof(InvalidNotificationDefinitions))]
    public void Work_Center_preference_registry_rejects_invalid_contracts(
        WorkCenterPreferenceDefinition definition,
        string message)
    {
        var action = () => new WorkCenterPreferenceDefinitionRegistry(
        [
            new TestNotificationSource(definition)
        ]);

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage(message);
    }

    [Fact]
    public void Work_Center_preference_registry_rejects_case_insensitive_duplicates()
    {
        var action = () => new WorkCenterPreferenceDefinitionRegistry(
        [
            new TestNotificationSource(Notification("test.ready", "Ready", "Test")),
            new TestNotificationSource(Notification("TEST.READY".ToLowerInvariant(), "Also ready", "Test"))
        ]);

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*more than once*");
    }

    public static IEnumerable<object[]> InvalidNotificationDefinitions()
    {
        yield return [Notification("", "Name", "Category"), "*canonical lowercase*"];
        yield return [Notification("Not.Canonical", "Name", "Category"), "*canonical lowercase*"];
        yield return [Notification("test.blank_name", " ", "Category"), "*display name and category*"];
        yield return [Notification("test.blank_category", "Name", " "), "*display name and category*"];
        yield return
        [
            Notification(
                "test.no_in_app",
                "Name",
                "Category",
                channels: new HashSet<NotificationChannel>()),
            "*must support the in-app channel*"
        ];
        yield return
        [
            Notification("test.mandatory", "Name", "Category", mandatory: true, canDisable: true),
            "*cannot be user-disableable*"
        ];
    }

    private static WorkCenterPreferenceDefinition Notification(
        string code,
        string displayName,
        string category,
        IReadOnlySet<NotificationChannel>? channels = null,
        bool mandatory = false,
        bool canDisable = true)
        => new(
            code,
            WorkCenterPreferenceKind.Notification,
            displayName,
            category,
            DefaultEnabled: true,
            UserCanDisable: canDisable,
            DefaultSeverity: NotificationSeverity.Information,
            SupportedChannels: channels ?? new HashSet<NotificationChannel> { NotificationChannel.InApp },
            Retention: null,
            IsMandatory: mandatory);

    private sealed class TestNotificationSource(params WorkCenterPreferenceDefinition[] definitions)
        : IWorkCenterPreferenceDefinitionSource
    {
        public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions() => definitions;
    }

    private sealed class InlineActionContributor(Action<DocumentActionDefinitionsBuilder> contribution)
        : IDocumentActionDefinitionsContributor
    {
        public void Contribute(DocumentActionDefinitionsBuilder builder) => contribution(builder);
    }

    private sealed class TestActionHandler : IDocumentActionHandler
    {
        public Task<DocumentActionHandlerResult> ExecuteAsync(
            DocumentActionHandlerContext context,
            CancellationToken ct)
            => Task.FromResult(new DocumentActionHandlerResult());
    }
}
