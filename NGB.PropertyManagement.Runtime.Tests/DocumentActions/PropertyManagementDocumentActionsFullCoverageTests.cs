using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Definitions.Documents.Actions;
using NGB.PropertyManagement.Runtime.DocumentActions;
using Xunit;
using ContractDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;
using CoreDocumentActionExecutionKind = NGB.Core.Documents.Actions.DocumentActionExecutionKind;
using StoredDocumentStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.PropertyManagement.Runtime.Tests.DocumentActions;

public sealed class PropertyManagementDocumentActionsFullCoverageTests
{
    [Fact]
    public void Codes_and_supported_document_type_lists_expose_the_complete_surface()
    {
        PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value
            .Should().Be("pm.open_receivables_reconciliation");
        PropertyManagementDocumentActionCodes.OpenPayablesReconciliation.Value
            .Should().Be("pm.open_payables_reconciliation");
        PropertyManagementDocumentActionDefinitionsContributor.ReceivableApplyDocumentTypes.Should().HaveCount(5);
        PropertyManagementDocumentActionDefinitionsContributor.PayableApplyDocumentTypes.Should().HaveCount(3);
    }

    [Fact]
    public void Contributor_registers_navigation_actions_for_every_supported_document_type()
    {
        var builder = new DocumentActionDefinitionsBuilder();

        new PropertyManagementDocumentActionDefinitionsContributor().Contribute(builder);
        var definitions = builder.Build();

        definitions.Should().HaveCount(8);
        definitions.Should().OnlyContain(x =>
            x.AvailabilityEvaluatorType == typeof(PropertyManagementApplyAvailabilityEvaluator)
            && x.Metadata.ExecutionKind == CoreDocumentActionExecutionKind.Navigation
            && x.Metadata.Target != null);
        definitions.Count(x => x.Metadata.Code == PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation)
            .Should().Be(5);
        definitions.Count(x => x.Metadata.Code == PropertyManagementDocumentActionCodes.OpenPayablesReconciliation)
            .Should().Be(3);
    }

    [Fact]
    public async Task Context_enricher_loads_allowed_and_disabled_facts_from_the_source()
    {
        var source = new Mock<IPropertyManagementApplyAvailabilitySource>(MockBehavior.Strict);
        var reason = new DocumentActionDisabledReasonDto("blocked", "Blocked");
        source.SetupSequence(x => x.EvaluateAsync(
                PropertyManagementCodes.ReceivablePayment, It.IsAny<Guid>(), ContractDocumentStatus.Posted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentActionAvailabilityResult.Allowed)
            .ReturnsAsync(new DocumentActionAvailabilityResult([reason]));
        var sut = new PropertyManagementApplyActionContextEnricher(
            PropertyManagementCodes.ReceivablePayment,
            source.Object);
        var request = Request(PropertyManagementCodes.ReceivablePayment);

        var allowed = await sut.LoadFactsAsync(request, default);
        var disabled = await sut.LoadFactsAsync(request, default);

        sut.DocumentTypeCode.Should().Be(PropertyManagementCodes.ReceivablePayment);
        allowed["pm.apply.allowed"].Should().Be(true);
        ((IReadOnlyList<DocumentActionDisabledReasonDto>)allowed["pm.apply.disabled_reasons"]!).Should().BeEmpty();
        disabled["pm.apply.allowed"].Should().Be(false);
        ((IReadOnlyList<DocumentActionDisabledReasonDto>)disabled["pm.apply.disabled_reasons"]!).Should().Equal(reason);
    }

    [Fact]
    public async Task Availability_evaluator_covers_allowed_configured_and_fallback_reasons()
    {
        var sut = new PropertyManagementApplyAvailabilityEvaluator();

        var allowed = await sut.EvaluateAsync(Context(new Dictionary<string, object?>
        {
            ["pm.apply.allowed"] = true
        }), default);
        var configuredReason = new DocumentActionDisabledReasonDto("configured", "Configured");
        var configured = await sut.EvaluateAsync(Context(new Dictionary<string, object?>
        {
            ["pm.apply.allowed"] = false,
            ["pm.apply.disabled_reasons"] = new[] { configuredReason }
        }), default);
        var missing = await sut.EvaluateAsync(Context(new Dictionary<string, object?>()), default);
        var wrongType = await sut.EvaluateAsync(Context(new Dictionary<string, object?>
        {
            ["pm.apply.disabled_reasons"] = "wrong"
        }), default);
        var nullAllowed = await sut.EvaluateAsync(Context(new Dictionary<string, object?>
        {
            ["pm.apply.allowed"] = null
        }), default);

        allowed.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
        configured.DisabledReasons.Should().Equal(configuredReason);
        missing.DisabledReasons.Should().ContainSingle(x => x.Code == "pm.apply.unavailable");
        wrongType.DisabledReasons.Should().ContainSingle(x => x.Code == "pm.apply.unavailable");
        nullAllowed.DisabledReasons.Should().ContainSingle(x => x.Code == "pm.apply.unavailable");
    }

    private static DocumentActionContextRequest Request(string type)
    {
        var (record, dto, security) = DocumentContext(type);
        return new DocumentActionContextRequest(record, dto, security);
    }

    private static DocumentActionEvaluationContext Context(IReadOnlyDictionary<string, object?> facts)
    {
        var (record, dto, security) = DocumentContext(PropertyManagementCodes.ReceivablePayment);
        return new DocumentActionEvaluationContext(record, dto, security, facts);
    }

    private static (DocumentRecord Record, DocumentDto Dto, DocumentActionSecurityContext Security) DocumentContext(string type)
    {
        var id = Guid.CreateVersion7();
        return (
            new DocumentRecord
            {
                Id = id,
                TypeCode = type,
                DateUtc = DateTime.UnixEpoch,
                Status = StoredDocumentStatus.Posted
            },
            new DocumentDto(id, "Document", new RecordPayload(), ContractDocumentStatus.Posted, false),
            new DocumentActionSecurityContext(null, false, false, false, new HashSet<NgbPermissionKey>()));
    }
}
