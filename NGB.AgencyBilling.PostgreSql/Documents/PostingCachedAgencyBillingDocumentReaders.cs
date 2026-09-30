using NGB.AgencyBilling.Documents;
using NGB.Persistence.Documents;

namespace NGB.AgencyBilling.PostgreSql.Documents;

internal sealed class PostingCachedAgencyBillingDocumentReaders(
    IAgencyBillingDocumentReaders inner,
    IDocumentPostingReadCache cache)
    : IAgencyBillingDocumentReaders
{
    public Task<AgencyBillingClientContractHead> ReadClientContractHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadClientContractHeadAsync), inner.ReadClientContractHeadAsync, ct);

    public Task<IReadOnlyList<AgencyBillingClientContractLine>> ReadClientContractLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadClientContractLinesAsync), inner.ReadClientContractLinesAsync, ct);

    public Task<AgencyBillingTimesheetHead> ReadTimesheetHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadTimesheetHeadAsync), inner.ReadTimesheetHeadAsync, ct);

    public Task<IReadOnlyDictionary<Guid, AgencyBillingTimesheetHead>> ReadTimesheetHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
        => inner.ReadTimesheetHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<AgencyBillingTimesheetLine>> ReadTimesheetLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadTimesheetLinesAsync), inner.ReadTimesheetLinesAsync, ct);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AgencyBillingTimesheetLine>>> ReadTimesheetLinesAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
        => inner.ReadTimesheetLinesAsync(documentIds, ct);

    public Task<AgencyBillingSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadSalesInvoiceHeadAsync), inner.ReadSalesInvoiceHeadAsync, ct);

    public Task<IReadOnlyDictionary<Guid, AgencyBillingSalesInvoiceHead>> ReadSalesInvoiceHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
        => inner.ReadSalesInvoiceHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<AgencyBillingSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadSalesInvoiceLinesAsync), inner.ReadSalesInvoiceLinesAsync, ct);

    public Task<AgencyBillingCustomerPaymentHead> ReadCustomerPaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadCustomerPaymentHeadAsync), inner.ReadCustomerPaymentHeadAsync, ct);

    public Task<IReadOnlyList<AgencyBillingCustomerPaymentApply>> ReadCustomerPaymentAppliesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadCustomerPaymentAppliesAsync), inner.ReadCustomerPaymentAppliesAsync, ct);

    private Task<T> ReadAsync<T>(
        Guid documentId,
        string operation,
        Func<Guid, CancellationToken, Task<T>> reader,
        CancellationToken ct)
        => cache.GetOrAddAsync(
            $"agency-billing:{operation}:{documentId:D}",
            innerCt => reader(documentId, innerCt),
            ct);
}
