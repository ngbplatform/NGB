namespace NGB.AgencyBilling.Documents;

public interface IAgencyBillingInvoiceUsageReader
{
    Task<AgencyBillingTimesheetInvoiceUsage> GetPostedInvoiceUsageForTimesheetAsync(
        Guid sourceTimesheetId,
        Guid? excludingSalesInvoiceId = null,
        CancellationToken ct = default);
}

public sealed record AgencyBillingTimesheetInvoiceUsage(decimal InvoicedHours, decimal InvoicedAmount);
