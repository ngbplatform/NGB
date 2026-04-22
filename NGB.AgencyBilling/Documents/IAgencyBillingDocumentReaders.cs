namespace NGB.AgencyBilling.Documents;

public interface IAgencyBillingDocumentReaders
{
    Task<AgencyBillingClientContractHead> ReadClientContractHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<AgencyBillingClientContractLine>> ReadClientContractLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<AgencyBillingTimesheetHead> ReadTimesheetHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<AgencyBillingTimesheetLine>> ReadTimesheetLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<AgencyBillingSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<AgencyBillingSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<AgencyBillingCustomerPaymentHead> ReadCustomerPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgencyBillingCustomerPaymentApply>> ReadCustomerPaymentAppliesAsync(
        Guid documentId,
        CancellationToken ct = default);
}
