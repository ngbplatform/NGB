using System.Reflection;
using FluentAssertions;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Actions;

public sealed class DocumentActionDefinitionAndCoreCoverageTests
{
    [Fact]
    public void Action_code_enforces_length_charset_and_value_equality()
    {
        var first = new DocumentActionCode("crm.convert-lead:v2");
        var second = new DocumentActionCode("crm.convert-lead:v2");

        first.Should().Be(second);
        first.Value.Should().Be("crm.convert-lead:v2");
        first.ToString().Should().Be(first.Value);

        var overlong = new string('a', DocumentActionCode.MaxLength + 1);
        var tooLong = () => new DocumentActionCode(overlong);
        tooLong.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage($"*{DocumentActionCode.MaxLength}*");
    }

    [Fact]
    public void Action_security_requires_an_active_identity_or_bootstrap_admin()
    {
        var permission = new NgbPermissionKey("document", "crm.lead", "execute");
        var userId = Guid.NewGuid();
        var context = new DocumentActionSecurityContext(
            userId,
            true,
            true,
            false,
            new HashSet<NgbPermissionKey> { permission });

        new DocumentActionSecurityContext(null, false, true, false, new HashSet<NgbPermissionKey> { permission })
            .Has("document", "crm.lead", "execute").Should().BeFalse();
        new DocumentActionSecurityContext(Guid.NewGuid(), true, false, false, new HashSet<NgbPermissionKey> { permission })
            .Has("document", "crm.lead", "execute").Should().BeFalse();
        new DocumentActionSecurityContext(Guid.NewGuid(), true, true, false, new HashSet<NgbPermissionKey>())
            .Has("document", "crm.lead", "execute").Should().BeFalse();
        new DocumentActionSecurityContext(Guid.NewGuid(), true, true, false, new HashSet<NgbPermissionKey> { permission })
            .Has("document", "crm.lead", "execute").Should().BeTrue();
        new DocumentActionSecurityContext(Guid.NewGuid(), true, true, true, new HashSet<NgbPermissionKey>())
            .Has("document", "anything", "execute").Should().BeTrue();
        context.UserId.Should().Be(userId);
        context.IsAuthenticated.Should().BeTrue();
        context.IsActive.Should().BeTrue();
        context.IsBootstrapAdmin.Should().BeFalse();
        context.Permissions.Should().Contain(permission);
    }

    [Fact]
    public void Action_exceptions_publish_stable_codes_and_non_sensitive_context()
    {
        var documentId = Guid.NewGuid();
        var cases = new (NgbException Error, string Code, string ContextKey, object ContextValue)[]
        {
            (new DocumentActionNotFoundException("crm.lead", "convert"), "document_action.not_found", "actionCode", "convert"),
            (new DocumentActionForbiddenException("crm.lead", "convert"), "document_action.forbidden", "documentType", "crm.lead"),
            (new DocumentActionUnavailableException("crm.lead", "convert", ["not_qualified"]), "document_action.unavailable", "reasonCodes", new[] { "not_qualified" }),
            (new DocumentVersionConflictException(documentId, 2, 3), "document.version_conflict", "documentId", documentId),
            (new DocumentActionIdempotencyConflictException("key-1"), "document_action.idempotency_conflict", "idempotencyKey", "key-1"),
            (new DocumentActionInProgressException("key-2"), "document_action.in_progress", "idempotencyKey", "key-2")
        };

        foreach (var item in cases)
        {
            item.Error.ErrorCode.Should().Be(item.Code);
            item.Error.Context.Should().ContainKey(item.ContextKey);
            if (item.ContextValue is string[] expectedReasons)
                item.Error.Context[item.ContextKey].Should().BeEquivalentTo(expectedReasons);
            else
                item.Error.Context[item.ContextKey].Should().Be(item.ContextValue);
        }
    }

    [Fact]
    public void Definition_builder_orders_definitions_and_normalizes_derivation_code()
    {
        var builder = new DocumentActionDefinitionsBuilder();
        builder.Add("z.document", Metadata("second", order: 20), derivationCode: "  derive.second  ");
        builder.Add("A.Document", Metadata("first", order: 10));
        builder.Add("a.document", Metadata("after", order: 20));

        var definitions = builder.Build();

        definitions.Select(static item => item.Metadata.Code.Value)
            .Should().Equal("first", "after", "second");
        definitions[0].DerivationCode.Should().BeNull();
        definitions[2].DerivationCode.Should().Be("derive.second");
    }

    [Fact]
    public void Definition_builder_rejects_missing_inputs_and_case_insensitive_duplicates()
    {
        var builder = new DocumentActionDefinitionsBuilder();
        var missingDocumentType = () => builder.Add(" ", Metadata("action"));
        missingDocumentType.Should().Throw<NgbArgumentInvalidException>();

        var missingMetadata = () => builder.Add("document", null!);
        missingMetadata.Should().Throw<NgbArgumentRequiredException>();

        builder.Add("CRM.LEAD", Metadata("crm.convert"));
        var duplicate = () => builder.Add("crm.lead", Metadata("crm.convert"));
        duplicate.Should().Throw<NgbConfigurationViolationException>()
            .Which.Context.Should().ContainKey("actionCode");
    }

    [Fact]
    public void Definition_builder_key_comparer_checks_document_and_action_components()
    {
        var comparerType = typeof(DocumentActionDefinitionsBuilder).GetNestedType(
            "DocumentActionDefinitionKeyComparer",
            BindingFlags.NonPublic);
        var instance = comparerType!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null);
        var comparer = (IEqualityComparer<(string DocumentType, string ActionCode)>)instance!;

        comparer.Equals(("crm.lead", "convert"), ("CRM.LEAD", "CONVERT")).Should().BeTrue();
        comparer.Equals(("crm.lead", "convert"), ("crm.lead", "qualify")).Should().BeFalse();
        comparer.Equals(("crm.lead", "convert"), ("crm.deal", "convert")).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public void Definition_builder_rejects_invalid_metadata(
        DocumentActionMetadata metadata,
        string expectedMessage)
    {
        var builder = new DocumentActionDefinitionsBuilder();

        var action = () => builder.Add("crm.lead", metadata);

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage(expectedMessage);
    }

    public static IEnumerable<object[]> InvalidMetadata()
    {
        yield return [Metadata("blank-label", label: " "), "*non-empty label*"];
        yield return [Metadata("negative", order: -1), "*cannot be negative*"];
        yield return
        [
            Metadata("navigate", executionKind: DocumentActionExecutionKind.Navigation),
            "*must define a target*"
        ];
        yield return
        [
            Metadata("view", executionKind: DocumentActionExecutionKind.View),
            "*must define a target*"
        ];
        yield return
        [
            Metadata(
                "redundant-confirm",
                confirmation: new DocumentActionConfirmationMetadata(
                    DocumentActionConfirmationMode.None,
                    "Title",
                    "Message",
                    "Confirm")),
            "*redundant confirmation*"
        ];
        yield return
        [
            Metadata(
                "blank-confirm-title",
                confirmation: new DocumentActionConfirmationMetadata(
                    DocumentActionConfirmationMode.Confirm,
                    " ",
                    "Message",
                    "Confirm")),
            "*must be non-empty*"
        ];
        yield return
        [
            Metadata(
                "blank-confirm-message",
                confirmation: new DocumentActionConfirmationMetadata(
                    DocumentActionConfirmationMode.Confirm,
                    "Title",
                    " ",
                    "Confirm")),
            "*must be non-empty*"
        ];
        yield return
        [
            Metadata(
                "blank-confirm-label",
                confirmation: new DocumentActionConfirmationMetadata(
                    DocumentActionConfirmationMode.RequireReason,
                    "Title",
                    "Message",
                    " ")),
            "*must be non-empty*"
        ];
    }

    private static DocumentActionMetadata Metadata(
        string code,
        int order = 10,
        string label = "Action",
        DocumentActionExecutionKind executionKind = DocumentActionExecutionKind.Command,
        DocumentActionConfirmationMetadata? confirmation = null)
        => new(
            new DocumentActionCode(code),
            new DocumentActionPresentation(label),
            DocumentActionKind.Secondary,
            executionKind,
            order,
            confirmation,
            Target: null);
}
