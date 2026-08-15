using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.DocumentActions;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;

namespace NGB.CRM.Runtime.Tests.DocumentActions;

public sealed class CrmDocumentDerivationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Contributor_registers_both_CRM_derivations()
    {
        var builder = new DefinitionsBuilder();

        new CrmDocumentDerivationDefinitionsContributor().Contribute(builder);
        var definitions = builder.Build();

        var qualification = definitions.GetDocumentDerivation(CrmDocumentActionCodes.CreateQualification);
        qualification.Name.Should().Be("Create qualification");
        qualification.FromTypeCode.Should().Be(CrmCodes.LeadIntake);
        qualification.ToTypeCode.Should().Be(CrmCodes.LeadQualification);
        qualification.RelationshipCodes.Should().Equal("qualifies");
        qualification.HandlerType.Should().Be(typeof(CrmLeadQualificationDerivationHandler));

        var conversion = definitions.GetDocumentDerivation(CrmDocumentActionCodes.CreateConversion);
        conversion.Name.Should().Be("Create conversion");
        conversion.FromTypeCode.Should().Be(CrmCodes.LeadQualification);
        conversion.ToTypeCode.Should().Be(CrmCodes.LeadConversion);
        conversion.RelationshipCodes.Should().Equal("based_on");
        conversion.HandlerType.Should().Be(typeof(CrmLeadConversionDerivationHandler));
    }

    [Theory]
    [InlineData("crm.wrong", CrmCodes.LeadQualification)]
    [InlineData(CrmCodes.LeadIntake, "crm.wrong")]
    public async Task Qualification_rejects_invalid_source_target_binding(string sourceType, string targetType)
    {
        var sut = CreateQualification();
        var context = Context(sourceType, targetType, DocumentStatus.Posted);

        await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

        sut.Relationships.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Qualification_requires_posted_source()
    {
        var sut = CreateQualification();
        var context = Context(CrmCodes.LeadIntake, CrmCodes.LeadQualification, DocumentStatus.Draft);

        var error = await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        error.Which.Operation.Should().Be("CRM.CreateQualification");
        error.Which.ExpectedState.Should().Be(nameof(DocumentStatus.Posted));
        error.Which.ActualState.Should().Be(nameof(DocumentStatus.Draft));
    }

    [Fact]
    public async Task Qualification_rejects_an_existing_qualification()
    {
        var sut = CreateQualification();
        var context = Context(CrmCodes.LeadIntake, CrmCodes.LeadQualification);
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                context.SourceDocument.Id,
                "qualifies",
                CancellationToken.None))
            .ReturnsAsync(true);

        var error = await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbException>();

        error.Which.ErrorCode.Should().Be("crm.lead_qualification.already_exists");
        sut.Readers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Qualification_fails_when_target_metadata_is_missing()
    {
        var sut = CreateQualification();
        var context = Context(CrmCodes.LeadIntake, CrmCodes.LeadQualification);
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                context.SourceDocument.Id,
                "qualifies",
                CancellationToken.None))
            .ReturnsAsync(false);
        sut.Readers
            .Setup(readers => readers.ReadLeadIntakeHeadAsync(context.SourceDocument.Id, CancellationToken.None))
            .ReturnsAsync(Lead(context.SourceDocument.Id));
        sut.DocumentTypes
            .Setup(registry => registry.TryGet(CrmCodes.LeadQualification))
            .Returns((DocumentTypeMetadata?)null);

        await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public async Task Qualification_prefills_head_and_updates_draft_header()
    {
        var sut = CreateQualification();
        var context = Context(CrmCodes.LeadIntake, CrmCodes.LeadQualification);
        var lead = Lead(context.SourceDocument.Id);
        IReadOnlyList<DocumentHeadValue>? captured = null;
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                context.SourceDocument.Id,
                "qualifies",
                CancellationToken.None))
            .ReturnsAsync(false);
        sut.Readers
            .Setup(readers => readers.ReadLeadIntakeHeadAsync(context.SourceDocument.Id, CancellationToken.None))
            .ReturnsAsync(lead);
        sut.DocumentTypes
            .Setup(registry => registry.TryGet(CrmCodes.LeadQualification))
            .Returns(Metadata(CrmCodes.LeadQualification));
        sut.Writer
            .Setup(writer => writer.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                context.TargetDraft.Id,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                CancellationToken.None))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>(
                (_, _, values, _) => captured = values)
            .Returns(Task.CompletedTask);
        sut.Documents
            .Setup(repository => repository.UpdateDraftHeaderAsync(
                context.TargetDraft.Id,
                context.TargetDraft.Number,
                new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                Now.UtcDateTime,
                CancellationToken.None))
            .ReturnsAsync(true);

        await sut.Handler.ApplyAsync(context);

        captured.Should().NotBeNull();
        captured!.Should().Contain(value =>
            value.ColumnName == "lead_intake_id" && Equals(value.Value, lead.DocumentId));
        captured.Should().Contain(value =>
            value.ColumnName == "qualification_state" && Equals(value.Value, "New"));
        captured.Should().Contain(value =>
            value.ColumnName == "score" && Equals(value.Value, 0));
        captured.Should().Contain(value =>
            value.ColumnName == "notes" && Equals(value.Value, "Qualification created from Acme lead."));
        sut.Documents.VerifyAll();
    }

    [Theory]
    [InlineData("crm.wrong", CrmCodes.LeadConversion)]
    [InlineData(CrmCodes.LeadQualification, "crm.wrong")]
    public async Task Conversion_rejects_invalid_source_target_binding(string sourceType, string targetType)
    {
        var sut = CreateConversion();
        var context = Context(sourceType, targetType, DocumentStatus.Posted);

        await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

        sut.Readers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Conversion_requires_posted_source()
    {
        var sut = CreateConversion();
        var context = Context(CrmCodes.LeadQualification, CrmCodes.LeadConversion, DocumentStatus.Draft);

        var error = await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        error.Which.Operation.Should().Be("CRM.CreateConversion");
    }

    [Fact]
    public async Task Conversion_requires_qualified_state()
    {
        var sut = CreateConversion();
        var context = Context(CrmCodes.LeadQualification, CrmCodes.LeadConversion);
        sut.Readers
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(
                context.SourceDocument.Id,
                CancellationToken.None))
            .ReturnsAsync(Qualification(context.SourceDocument.Id, Guid.NewGuid(), "Disqualified"));

        var error = await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbException>();

        error.Which.ErrorCode.Should().Be("crm.lead_conversion.qualification_not_qualified");
        sut.Relationships.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Conversion_rejects_already_converted_lead()
    {
        var sut = CreateConversion();
        var context = Context(CrmCodes.LeadQualification, CrmCodes.LeadConversion);
        var leadId = Guid.NewGuid();
        sut.Readers
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(
                context.SourceDocument.Id,
                CancellationToken.None))
            .ReturnsAsync(Qualification(context.SourceDocument.Id, leadId, "QUALIFIED"));
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                leadId,
                "converts",
                CancellationToken.None))
            .ReturnsAsync(true);

        var error = await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbException>();

        error.Which.ErrorCode.Should().Be("crm.lead_conversion.already_exists");
    }

    [Fact]
    public async Task Conversion_fails_when_target_metadata_is_missing()
    {
        var sut = CreateConversion();
        var context = Context(CrmCodes.LeadQualification, CrmCodes.LeadConversion);
        var leadId = Guid.NewGuid();
        sut.Readers
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(
                context.SourceDocument.Id,
                CancellationToken.None))
            .ReturnsAsync(Qualification(context.SourceDocument.Id, leadId, "Qualified"));
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                leadId,
                "converts",
                CancellationToken.None))
            .ReturnsAsync(false);
        sut.Readers
            .Setup(readers => readers.ReadLeadIntakeHeadAsync(leadId, CancellationToken.None))
            .ReturnsAsync(Lead(leadId));
        sut.DocumentTypes
            .Setup(registry => registry.TryGet(CrmCodes.LeadConversion))
            .Returns((DocumentTypeMetadata?)null);

        await FluentActions.Awaiting(() => sut.Handler.ApplyAsync(context))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData(null, CrmCodes.DefaultCurrency)]
    public async Task Conversion_prefills_head_creates_relationship_and_updates_header(
        string? sourceCurrency,
        string expectedCurrency)
    {
        var sut = CreateConversion();
        var context = Context(CrmCodes.LeadQualification, CrmCodes.LeadConversion);
        var leadId = Guid.NewGuid();
        var qualification = Qualification(context.SourceDocument.Id, leadId, "Qualified");
        var lead = Lead(leadId) with { Currency = sourceCurrency };
        IReadOnlyList<DocumentHeadValue>? captured = null;
        sut.Readers
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(
                context.SourceDocument.Id,
                CancellationToken.None))
            .ReturnsAsync(qualification);
        sut.Relationships
            .Setup(service => service.ExistsIncomingAsync(
                leadId,
                "converts",
                CancellationToken.None))
            .ReturnsAsync(false);
        sut.Readers
            .Setup(readers => readers.ReadLeadIntakeHeadAsync(leadId, CancellationToken.None))
            .ReturnsAsync(lead);
        sut.DocumentTypes
            .Setup(registry => registry.TryGet(CrmCodes.LeadConversion))
            .Returns(Metadata(CrmCodes.LeadConversion));
        sut.Writer
            .Setup(writer => writer.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                context.TargetDraft.Id,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                CancellationToken.None))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>(
                (_, _, values, _) => captured = values)
            .Returns(Task.CompletedTask);
        sut.Relationships
            .Setup(service => service.CreateAsync(
                context.TargetDraft.Id,
                leadId,
                "converts",
                false,
                CancellationToken.None))
            .ReturnsAsync(true);
        sut.Documents
            .Setup(repository => repository.UpdateDraftHeaderAsync(
                context.TargetDraft.Id,
                context.TargetDraft.Number,
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
                Now.UtcDateTime,
                CancellationToken.None))
            .ReturnsAsync(true);

        await sut.Handler.ApplyAsync(context);

        captured.Should().NotBeNull();
        captured!.Should().Contain(value =>
            value.ColumnName == "lead_intake_id" && Equals(value.Value, leadId));
        captured.Should().Contain(value =>
            value.ColumnName == "create_opportunity" && Equals(value.Value, false));
        captured.Should().Contain(value =>
            value.ColumnName == "opportunity_name" && Equals(value.Value, lead.LeadName));
        captured.Should().Contain(value =>
            value.ColumnName == "amount" && Equals(value.Value, lead.EstimatedValue));
        captured.Should().Contain(value =>
            value.ColumnName == "currency" && Equals(value.Value, expectedCurrency));
        sut.Relationships.VerifyAll();
        sut.Documents.VerifyAll();
    }

    private static (
        CrmLeadQualificationDerivationHandler Handler,
        Mock<ICrmDocumentReaders> Readers,
        Mock<IDocumentTypeRegistry> DocumentTypes,
        Mock<IDocumentWriter> Writer,
        Mock<IDocumentRepository> Documents,
        Mock<IDocumentRelationshipService> Relationships) CreateQualification()
    {
        var readers = Strict<ICrmDocumentReaders>();
        var documentTypes = Strict<IDocumentTypeRegistry>();
        var writer = Strict<IDocumentWriter>();
        var documents = Strict<IDocumentRepository>();
        var relationships = Strict<IDocumentRelationshipService>();
        return (
            new CrmLeadQualificationDerivationHandler(
                readers.Object,
                documentTypes.Object,
                writer.Object,
                documents.Object,
                relationships.Object,
                new FixedTimeProvider(Now)),
            readers,
            documentTypes,
            writer,
            documents,
            relationships);
    }

    private static (
        CrmLeadConversionDerivationHandler Handler,
        Mock<ICrmDocumentReaders> Readers,
        Mock<IDocumentTypeRegistry> DocumentTypes,
        Mock<IDocumentWriter> Writer,
        Mock<IDocumentRepository> Documents,
        Mock<IDocumentRelationshipService> Relationships) CreateConversion()
    {
        var readers = Strict<ICrmDocumentReaders>();
        var documentTypes = Strict<IDocumentTypeRegistry>();
        var writer = Strict<IDocumentWriter>();
        var documents = Strict<IDocumentRepository>();
        var relationships = Strict<IDocumentRelationshipService>();
        return (
            new CrmLeadConversionDerivationHandler(
                readers.Object,
                documentTypes.Object,
                writer.Object,
                documents.Object,
                relationships.Object,
                new FixedTimeProvider(Now)),
            readers,
            documentTypes,
            writer,
            documents,
            relationships);
    }

    private static Mock<T> Strict<T>() where T : class => new(MockBehavior.Strict);

    private static DocumentDerivationContext Context(
        string sourceType,
        string targetType,
        DocumentStatus sourceStatus = DocumentStatus.Posted)
        => new(
            "test",
            Document(sourceType, sourceStatus, "SRC-1"),
            Document(targetType, DocumentStatus.Draft, "DST-1"),
            []);

    private static DocumentRecord Document(string type, DocumentStatus status, string number)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = type,
            Number = number,
            DateUtc = Now.UtcDateTime,
            Status = status,
            Version = 1,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

    private static CrmLeadIntakeHead Lead(Guid id)
        => new(
            id,
            new DateOnly(2026, 7, 20),
            "Acme lead",
            "Acme",
            "Alice",
            "alice@example.com",
            null,
            "Referral",
            "Technology",
            25000m,
            "USD",
            null);

    private static CrmLeadQualificationHead Qualification(Guid id, Guid leadId, string state)
        => new(id, new DateOnly(2026, 7, 21), leadId, state, 90, null, null);

    private static DocumentTypeMetadata Metadata(string type)
        => new(
            type,
            [
                new(
                    $"doc_{type.Replace('.', '_')}",
                    TableKind.Head,
                    [
                        new("document_id", ColumnType.Guid),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.Date),
                        new("lead_intake_id", ColumnType.Guid),
                        new("qualification_state", ColumnType.String),
                        new("score", ColumnType.Int32),
                        new("notes", ColumnType.String),
                        new("create_opportunity", ColumnType.Boolean),
                        new("opportunity_name", ColumnType.String),
                        new("amount", ColumnType.Decimal),
                        new("currency", ColumnType.String)
                    ])
            ]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
