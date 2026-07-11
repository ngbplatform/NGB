using NGB.CRM.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.CRM.Runtime.Documents.Validation;

public sealed class LeadIntakePostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.LeadIntake;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadLeadIntakeHeadAsync(documentForUpdate.Id, ct);

        if (string.IsNullOrWhiteSpace(head.LeadName))
            throw new NgbArgumentInvalidException("lead_name", "Lead Name is required.");

        if (string.IsNullOrWhiteSpace(head.ContactName))
            throw new NgbArgumentInvalidException("contact_name", "Contact Name is required.");

        if (head.EstimatedValue is < 0m)
            throw new NgbArgumentInvalidException("estimated_value", "Estimated Value must be zero or greater.");
    }
}

public sealed class LeadQualificationPostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.LeadQualification;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadLeadQualificationHeadAsync(documentForUpdate.Id, ct);

        if (head.Score is < 0 or > 100)
            throw new NgbArgumentInvalidException("score", "Score must be between 0 and 100.");

        if (string.Equals(head.QualificationState, "Disqualified", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(head.DisqualificationReason))
        {
            throw new NgbArgumentInvalidException(
                "disqualification_reason",
                "Disqualification Reason is required when state is Disqualified.");
        }
    }
}

public sealed class LeadConversionPostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.LeadConversion;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadLeadConversionHeadAsync(documentForUpdate.Id, ct);

        if (!head.CreateOpportunity)
            return;

        if (string.IsNullOrWhiteSpace(head.OpportunityName))
            throw new NgbArgumentInvalidException("opportunity_name", "Opportunity Name is required when Create Opportunity is enabled.");

        if (head.StageId is null)
            throw new NgbArgumentInvalidException("stage_id", "Stage is required when Create Opportunity is enabled.");

        CrmPostValidation.ValidateProbability(head.Probability, "probability");
        CrmPostValidation.ValidateAmount(head.Amount, "amount");
    }
}

public sealed class OpportunityUpdatePostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.OpportunityUpdate;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadOpportunityUpdateHeadAsync(documentForUpdate.Id, ct);

        CrmPostValidation.ValidateAmount(head.Amount, "amount");
        CrmPostValidation.ValidateProbability(head.Probability, "probability");

        if (string.Equals(head.Status, "Lost", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(head.LossReason))
        {
            throw new NgbArgumentInvalidException("loss_reason", "Loss Reason is required when Status is Lost.");
        }
    }
}

public sealed class QuotePostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.Quote;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadQuoteHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadQuoteLinesAsync(documentForUpdate.Id, ct);

        if (head.ValidUntil < head.DocumentDateUtc)
            throw new NgbArgumentInvalidException("valid_until", "Valid Until must be on or after Document Date.");

        if (head.Amount < 0m)
            throw new NgbArgumentInvalidException("amount", "Quote Amount must be zero or greater.");

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Quote must contain at least one line.");

        foreach (var line in lines)
        {
            var prefix = $"lines[{line.Ordinal}]";

            if (line.Ordinal <= 0)
                throw new NgbArgumentInvalidException($"{prefix}.ordinal", "Line ordinal must be greater than zero.");

            if (line.Quantity <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.quantity", "Quantity must be greater than zero.");

            if (line.UnitPrice < 0m)
                throw new NgbArgumentInvalidException($"{prefix}.unit_price", "Unit Price must be zero or greater.");

            if (line.DiscountPercent is < 0m or > 100m)
                throw new NgbArgumentInvalidException($"{prefix}.discount_percent", "Discount Percent must be between 0 and 100.");

            if (line.LineAmount < 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount must be zero or greater.");
        }
    }
}

public sealed class ActivityLogPostValidator(ICrmDocumentReaders readers) : IDocumentPostValidator
{
    public string TypeCode => CrmCodes.ActivityLog;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        var head = await readers.ReadActivityLogHeadAsync(documentForUpdate.Id, ct);

        if (string.IsNullOrWhiteSpace(head.Subject))
            throw new NgbArgumentInvalidException("subject", "Subject is required.");

        if (head.LeadIntakeId is null
            && head.AccountId is null
            && head.ContactId is null
            && head.OpportunityId is null)
        {
            throw new NgbArgumentInvalidException(
                "related_entity",
                "Activity must reference at least one lead, account, contact, or opportunity.");
        }
    }
}

file static class CrmPostValidation
{
    public static void ValidateAmount(decimal? value, string field)
    {
        if (value is < 0m)
            throw new NgbArgumentInvalidException(field, "Amount must be zero or greater.");
    }

    public static void ValidateProbability(decimal? value, string field)
    {
        if (value is < 0m or > 100m)
            throw new NgbArgumentInvalidException(field, "Probability must be between 0 and 100.");
    }
}
