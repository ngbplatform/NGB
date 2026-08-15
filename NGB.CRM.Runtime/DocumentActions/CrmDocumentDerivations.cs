using NGB.Core.Documents;
using NGB.CRM.Documents;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;

namespace NGB.CRM.Runtime.DocumentActions;

public static class CrmDocumentActionCodes
{
    public const string CreateQualification = "crm.create_qualification";
    public const string CreateConversion = "crm.create_conversion";
}

public sealed class CrmDocumentDerivationDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocumentDerivation(CrmDocumentActionCodes.CreateQualification, d => d
            .Name("Create qualification")
            .From(CrmCodes.LeadIntake)
            .To(CrmCodes.LeadQualification)
            .Relationship("qualifies")
            .Handler<CrmLeadQualificationDerivationHandler>());

        builder.AddDocumentDerivation(CrmDocumentActionCodes.CreateConversion, d => d
            .Name("Create conversion")
            .From(CrmCodes.LeadQualification)
            .To(CrmCodes.LeadConversion)
            .Relationship("based_on")
            .Handler<CrmLeadConversionDerivationHandler>());
    }
}

public sealed class CrmLeadQualificationDerivationHandler(
    ICrmDocumentReaders readers,
    IDocumentTypeRegistry documentTypes,
    IDocumentWriter writer,
    IDocumentRepository documents,
    IDocumentRelationshipService relationships,
    TimeProvider timeProvider)
    : IDocumentDerivationHandler
{
    public async Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default)
    {
        EnsureSource(ctx, CrmCodes.LeadIntake, CrmCodes.LeadQualification, "CRM.CreateQualification");
        if (await relationships.ExistsIncomingAsync(ctx.SourceDocument.Id, "qualifies", ct))
        {
            throw new CrmDerivationConflictException(
                "A qualification already exists for this lead.",
                "crm.lead_qualification.already_exists");
        }

        var lead = await readers.ReadLeadIntakeHeadAsync(ctx.SourceDocument.Id, ct);
        var metadata = documentTypes.TryGet(CrmCodes.LeadQualification)
            ?? throw new NgbConfigurationViolationException($"Document type '{CrmCodes.LeadQualification}' is not registered.");

        await writer.UpsertHeadAsync(
            metadata.CreateHeadDescriptor(),
            ctx.TargetDraft.Id,
            [
                new("document_date_utc", ColumnType.Date, lead.DocumentDateUtc),
                new("lead_intake_id", ColumnType.Guid, lead.DocumentId),
                new("qualification_state", ColumnType.String, "New"),
                new("score", ColumnType.Int32, 0),
                new("notes", ColumnType.String, $"Qualification created from {lead.LeadName}.")
            ],
            ct);

        await documents.UpdateDraftHeaderAsync(
            ctx.TargetDraft.Id,
            ctx.TargetDraft.Number,
            ToUtc(lead.DocumentDateUtc),
            timeProvider.GetUtcNow().UtcDateTime,
            ct);
    }

    private static void EnsureSource(
        DocumentDerivationContext context,
        string expectedSource,
        string expectedTarget,
        string operation)
    {
        if (!string.Equals(context.SourceDocument.TypeCode, expectedSource, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(context.TargetDraft.TypeCode, expectedTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException("CRM derivation source/target binding is invalid.");
        }

        if (context.SourceDocument.Status != DocumentStatus.Posted)
        {
            throw new DocumentWorkflowStateMismatchException(
                operation,
                context.SourceDocument.Id,
                nameof(DocumentStatus.Posted),
                context.SourceDocument.Status.ToString());
        }
    }

    private static DateTime ToUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}

public sealed class CrmLeadConversionDerivationHandler(
    ICrmDocumentReaders readers,
    IDocumentTypeRegistry documentTypes,
    IDocumentWriter writer,
    IDocumentRepository documents,
    IDocumentRelationshipService relationships,
    TimeProvider timeProvider)
    : IDocumentDerivationHandler
{
    public async Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default)
    {
        if (!string.Equals(ctx.SourceDocument.TypeCode, CrmCodes.LeadQualification, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ctx.TargetDraft.TypeCode, CrmCodes.LeadConversion, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException("CRM conversion derivation source/target binding is invalid.");
        }

        if (ctx.SourceDocument.Status != DocumentStatus.Posted)
            throw new DocumentWorkflowStateMismatchException(
                "CRM.CreateConversion",
                ctx.SourceDocument.Id,
                nameof(DocumentStatus.Posted),
                ctx.SourceDocument.Status.ToString());

        var qualification = await readers.ReadLeadQualificationHeadAsync(ctx.SourceDocument.Id, ct);
        if (!string.Equals(qualification.QualificationState, "Qualified", StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmDerivationConflictException(
                "Only a Qualified lead can be converted.",
                "crm.lead_conversion.qualification_not_qualified");
        }

        if (await relationships.ExistsIncomingAsync(qualification.LeadIntakeId, "converts", ct))
        {
            throw new CrmDerivationConflictException(
                "This lead was already converted.",
                "crm.lead_conversion.already_exists");
        }

        var lead = await readers.ReadLeadIntakeHeadAsync(qualification.LeadIntakeId, ct);
        var metadata = documentTypes.TryGet(CrmCodes.LeadConversion) 
            ?? throw new NgbConfigurationViolationException($"Document type '{CrmCodes.LeadConversion}' is not registered.");

        await writer.UpsertHeadAsync(
            metadata.CreateHeadDescriptor(),
            ctx.TargetDraft.Id,
            [
                new("document_date_utc", ColumnType.Date, qualification.DocumentDateUtc),
                new("lead_intake_id", ColumnType.Guid, qualification.LeadIntakeId),
                new("create_opportunity", ColumnType.Boolean, false),
                new("opportunity_name", ColumnType.String, lead.LeadName),
                new("amount", ColumnType.Decimal, lead.EstimatedValue),
                new("currency", ColumnType.String, lead.Currency ?? CrmCodes.DefaultCurrency),
                new("notes", ColumnType.String, $"Conversion created from qualification {qualification.DocumentId}.")
            ],
            ct);

        await relationships.CreateAsync(
            ctx.TargetDraft.Id,
            qualification.LeadIntakeId,
            "converts",
            manageTransaction: false,
            ct);

        await documents.UpdateDraftHeaderAsync(
            ctx.TargetDraft.Id,
            ctx.TargetDraft.Number,
            DateTime.SpecifyKind(
                qualification.DocumentDateUtc.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc),
            timeProvider.GetUtcNow().UtcDateTime,
            ct);
    }
}

internal sealed class CrmDerivationConflictException(string message, string code)
    : NgbConflictException(message, code);
