using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.CRM.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Tools.Extensions;

namespace NGB.CRM.Runtime.Posting;

public sealed class LeadIntakeReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.LeadIntake;
}

public sealed class LeadQualificationReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.LeadQualification;
}

public sealed class LeadConversionReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.LeadConversion;
}

public sealed class OpportunityUpdateReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.OpportunityUpdate;
}

public sealed class QuoteReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.Quote;
}

public sealed class ActivityLogReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : CrmReferenceRegisterPostingHandler(readers, dimensionSets)
{
    public override string TypeCode => CrmCodes.ActivityLog;
}

public abstract class CrmReferenceRegisterPostingHandler(
    ICrmDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : IDocumentReferenceRegisterPostingHandler
{
    public abstract string TypeCode { get; }

    public async Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        switch (TypeCode)
        {
            case CrmCodes.LeadIntake:
                await BuildLeadIntakeAsync(document, operation, builder, ct);
                break;
            case CrmCodes.LeadQualification:
                await BuildLeadQualificationAsync(document, operation, builder, ct);
                break;
            case CrmCodes.LeadConversion:
                await BuildLeadConversionAsync(document, operation, builder, ct);
                break;
            case CrmCodes.OpportunityUpdate:
                await BuildOpportunityUpdateAsync(document, operation, builder, ct);
                break;
            case CrmCodes.Quote:
                await BuildQuoteAsync(document, operation, builder, ct);
                break;
            case CrmCodes.ActivityLog:
                await BuildActivityLogAsync(document, operation, builder, ct);
                break;
        }
    }

    private async Task BuildLeadIntakeAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var lead = await readers.ReadLeadIntakeHeadAsync(document.Id, ct);

        await AddLeadFunnelRecordAsync(
            builder,
            CrmCodes.LeadIntake,
            document.Id,
            lead,
            funnelStep: "01 Intake",
            qualificationState: "New",
            qualificationScore: null,
            convertedAccountId: null,
            convertedContactId: null,
            eventDateUtc: lead.DocumentDateUtc,
            operation,
            ct);
    }

    private async Task BuildLeadQualificationAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var qualification = await readers.ReadLeadQualificationHeadAsync(document.Id, ct);
        var lead = await readers.ReadLeadIntakeHeadAsync(qualification.LeadIntakeId, ct);

        await AddLeadFunnelRecordAsync(
            builder,
            CrmCodes.LeadQualification,
            document.Id,
            lead,
            funnelStep: QualificationFunnelStep(qualification.QualificationState),
            qualificationState: qualification.QualificationState,
            qualificationScore: qualification.Score,
            convertedAccountId: null,
            convertedContactId: null,
            eventDateUtc: qualification.DocumentDateUtc,
            operation,
            ct);
    }

    private async Task BuildLeadConversionAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var conversion = await readers.ReadLeadConversionHeadAsync(document.Id, ct);
        var lead = await readers.ReadLeadIntakeHeadAsync(conversion.LeadIntakeId, ct);

        await AddLeadFunnelRecordAsync(
            builder,
            CrmCodes.LeadConversion,
            document.Id,
            lead,
            funnelStep: "03 Converted",
            qualificationState: "Converted",
            qualificationScore: null,
            convertedAccountId: conversion.AccountId,
            convertedContactId: conversion.ContactId,
            eventDateUtc: conversion.DocumentDateUtc,
            operation,
            ct);

        if (!conversion.CreateOpportunity)
            return;

        builder.Add(
            CrmCodes.OpportunitiesRegisterCode,
            new ReferenceRegisterRecordWrite(
                DimensionSetId: await DimensionSetIdAsync(CrmCodes.LeadConversion, conversion.DocumentId, ct),
                PeriodUtc: null,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["opportunity_id"] = conversion.DocumentId,
                    ["source_document_id"] = document.Id,
                    ["event_type"] = "Conversion",
                    ["event_at_utc"] = EventAtUtc(conversion.DocumentDateUtc),
                    ["opportunity_name"] = NonBlank(conversion.OpportunityName, "Opportunity"),
                    ["account_id"] = conversion.AccountId,
                    ["contact_id"] = conversion.ContactId,
                    ["stage_id"] = conversion.StageId!.Value,
                    ["amount"] = conversion.Amount ?? 0m,
                    ["probability"] = conversion.Probability ?? 0m,
                    ["expected_close_date"] = conversion.ExpectedCloseDate,
                    ["status"] = "Open",
                    ["loss_reason"] = null,
                    ["currency"] = Currency(conversion.Currency),
                    ["updated_at_utc"] = DateTime.UtcNow
                },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
    }

    private async Task BuildOpportunityUpdateAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var update = await readers.ReadOpportunityUpdateHeadAsync(document.Id, ct);
        var conversion = await readers.ReadLeadConversionHeadAsync(update.OpportunityId, ct);

        builder.Add(
            CrmCodes.OpportunitiesRegisterCode,
            new ReferenceRegisterRecordWrite(
                DimensionSetId: await DimensionSetIdAsync(CrmCodes.LeadConversion, update.OpportunityId, ct),
                PeriodUtc: null,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["opportunity_id"] = update.OpportunityId,
                    ["source_document_id"] = document.Id,
                    ["event_type"] = "Update",
                    ["event_at_utc"] = EventAtUtc(update.DocumentDateUtc),
                    ["opportunity_name"] = NonBlank(conversion.OpportunityName, "Opportunity"),
                    ["account_id"] = conversion.AccountId,
                    ["contact_id"] = conversion.ContactId,
                    ["stage_id"] = update.StageId,
                    ["amount"] = update.Amount,
                    ["probability"] = update.Probability,
                    ["expected_close_date"] = update.ExpectedCloseDate,
                    ["status"] = update.Status,
                    ["loss_reason"] = update.LossReason,
                    ["currency"] = Currency(conversion.Currency),
                    ["updated_at_utc"] = DateTime.UtcNow
                },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
    }

    private async Task BuildQuoteAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var quote = await readers.ReadQuoteHeadAsync(document.Id, ct);

        builder.Add(
            CrmCodes.QuotesRegisterCode,
            new ReferenceRegisterRecordWrite(
                DimensionSetId: await DimensionSetIdAsync(CrmCodes.Quote, quote.DocumentId, ct),
                PeriodUtc: null,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["quote_id"] = quote.DocumentId,
                    ["source_document_id"] = document.Id,
                    ["opportunity_id"] = quote.OpportunityId,
                    ["account_id"] = quote.AccountId,
                    ["contact_id"] = quote.ContactId,
                    ["quote_date"] = quote.DocumentDateUtc,
                    ["valid_until"] = quote.ValidUntil,
                    ["currency"] = Currency(quote.Currency),
                    ["quote_status"] = quote.QuoteStatus,
                    ["amount"] = quote.Amount,
                    ["updated_at_utc"] = DateTime.UtcNow
                },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
    }

    private async Task BuildActivityLogAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var activity = await readers.ReadActivityLogHeadAsync(document.Id, ct);

        builder.Add(
            CrmCodes.ActivitiesRegisterCode,
            new ReferenceRegisterRecordWrite(
                DimensionSetId: await DimensionSetIdAsync(CrmCodes.ActivityLog, activity.DocumentId, ct),
                PeriodUtc: null,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["activity_id"] = activity.DocumentId,
                    ["source_document_id"] = document.Id,
                    ["activity_date"] = activity.DocumentDateUtc,
                    ["activity_type"] = activity.ActivityType,
                    ["subject"] = activity.Subject,
                    ["lead_intake_id"] = activity.LeadIntakeId,
                    ["account_id"] = activity.AccountId,
                    ["contact_id"] = activity.ContactId,
                    ["opportunity_id"] = activity.OpportunityId,
                    ["due_at_utc"] = activity.DueAtUtc,
                    ["completed_at_utc"] = activity.CompletedAtUtc,
                    ["outcome"] = activity.Outcome,
                    ["updated_at_utc"] = DateTime.UtcNow
                },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
    }

    private async Task AddLeadFunnelRecordAsync(
        IReferenceRegisterRecordsBuilder builder,
        string sourceDimensionCode,
        Guid sourceDocumentId,
        CrmLeadIntakeHead lead,
        string funnelStep,
        string? qualificationState,
        int? qualificationScore,
        Guid? convertedAccountId,
        Guid? convertedContactId,
        DateOnly eventDateUtc,
        ReferenceRegisterWriteOperation operation,
        CancellationToken ct)
    {
        builder.Add(
            CrmCodes.LeadFunnelRegisterCode,
            new ReferenceRegisterRecordWrite(
                DimensionSetId: await DimensionSetIdAsync(sourceDimensionCode, sourceDocumentId, ct),
                PeriodUtc: null,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lead_intake_id"] = lead.DocumentId,
                    ["source_document_id"] = sourceDocumentId,
                    ["funnel_step"] = funnelStep,
                    ["lead_name"] = lead.LeadName,
                    ["company_name"] = lead.CompanyName,
                    ["contact_name"] = lead.ContactName,
                    ["email"] = lead.Email,
                    ["lead_source"] = lead.LeadSource,
                    ["industry"] = lead.Industry,
                    ["qualification_state"] = qualificationState,
                    ["qualification_score"] = qualificationScore,
                    ["converted_account_id"] = convertedAccountId,
                    ["converted_contact_id"] = convertedContactId,
                    ["event_at_utc"] = EventAtUtc(eventDateUtc),
                    ["updated_at_utc"] = DateTime.UtcNow
                },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
    }

    private async Task<Guid> DimensionSetIdAsync(string dimensionCode, Guid valueId, CancellationToken ct)
    {
        var bag = new DimensionBag(
        [
            new DimensionValue(DeterministicGuid.Create($"Dimension|{dimensionCode}"), valueId)
        ]);

        return await dimensionSets.GetOrCreateIdAsync(bag, ct);
    }

    private static DateTime EventAtUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static string QualificationFunnelStep(string state)
        => state switch
        {
            "Qualified" => "02 Qualified",
            "Disqualified" => "02 Disqualified",
            "Converted" => "03 Converted",
            _ => $"02 {state}"
        };

    private static string Currency(string? currency)
        => string.IsNullOrWhiteSpace(currency)
            ? CrmCodes.DefaultCurrency
            : currency.Trim().ToUpperInvariant();

    private static string NonBlank(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
