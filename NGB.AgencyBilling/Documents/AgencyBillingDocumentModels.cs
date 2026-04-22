using NGB.AgencyBilling.Enums;

namespace NGB.AgencyBilling.Documents;

public sealed record AgencyBillingClientContractHead(
    Guid DocumentId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid ClientId,
    Guid ProjectId,
    string CurrencyCode,
    AgencyBillingContractBillingFrequency BillingFrequency,
    Guid? PaymentTermsId,
    string? InvoiceMemoTemplate,
    bool IsActive,
    string? Notes);

public sealed record AgencyBillingClientContractLine(
    Guid DocumentId,
    int Ordinal,
    Guid? ServiceItemId,
    Guid? TeamMemberId,
    string? ServiceTitle,
    decimal BillingRate,
    decimal? CostRate,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    string? Notes);

public sealed record AgencyBillingTimesheetHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid TeamMemberId,
    Guid ProjectId,
    Guid ClientId,
    DateOnly WorkDate,
    decimal TotalHours,
    decimal Amount,
    decimal CostAmount,
    string? Notes);

public sealed record AgencyBillingTimesheetLine(
    Guid DocumentId,
    int Ordinal,
    Guid? ServiceItemId,
    string? Description,
    decimal Hours,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    decimal? LineAmount,
    decimal? LineCostAmount);

public sealed record AgencyBillingSalesInvoiceHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    DateOnly DueDate,
    Guid ClientId,
    Guid ProjectId,
    Guid? ContractId,
    string CurrencyCode,
    string? Memo,
    decimal Amount,
    string? Notes);

public sealed record AgencyBillingSalesInvoiceLine(
    Guid DocumentId,
    int Ordinal,
    Guid? ServiceItemId,
    Guid? SourceTimesheetId,
    string Description,
    decimal QuantityHours,
    decimal Rate,
    decimal LineAmount);

public sealed record AgencyBillingCustomerPaymentHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid ClientId,
    Guid? CashAccountId,
    string? ReferenceNumber,
    decimal Amount,
    string? Notes);

public sealed record AgencyBillingCustomerPaymentApply(
    Guid DocumentId,
    int Ordinal,
    Guid SalesInvoiceId,
    decimal AppliedAmount);
