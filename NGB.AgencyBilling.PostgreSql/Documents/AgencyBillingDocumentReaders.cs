using Dapper;
using NGB.AgencyBilling.Documents;
using NGB.Persistence.UnitOfWork;

namespace NGB.AgencyBilling.PostgreSql.Documents;

public sealed class AgencyBillingDocumentReaders(IUnitOfWork uow) : IAgencyBillingDocumentReaders
{
    public async Task<AgencyBillingClientContractHead> ReadClientContractHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    effective_from AS EffectiveFrom,
    effective_to AS EffectiveTo,
    client_id AS ClientId,
    project_id AS ProjectId,
    currency_code AS CurrencyCode,
    billing_frequency AS BillingFrequency,
    payment_terms_id AS PaymentTermsId,
    invoice_memo_template AS InvoiceMemoTemplate,
    is_active AS IsActive,
    notes AS Notes
FROM doc_ab_client_contract
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<AgencyBillingClientContractHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AgencyBillingClientContractLine>> ReadClientContractLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    service_item_id AS ServiceItemId,
    team_member_id AS TeamMemberId,
    service_title AS ServiceTitle,
    billing_rate AS BillingRate,
    cost_rate AS CostRate,
    active_from AS ActiveFrom,
    active_to AS ActiveTo,
    notes AS Notes
FROM doc_ab_client_contract__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<AgencyBillingClientContractLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<AgencyBillingTimesheetHead> ReadTimesheetHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    team_member_id AS TeamMemberId,
    project_id AS ProjectId,
    client_id AS ClientId,
    work_date AS WorkDate,
    total_hours AS TotalHours,
    amount AS Amount,
    cost_amount AS CostAmount,
    notes AS Notes
FROM doc_ab_timesheet
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<AgencyBillingTimesheetHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AgencyBillingTimesheetLine>> ReadTimesheetLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    service_item_id AS ServiceItemId,
    description AS Description,
    hours AS Hours,
    billable AS Billable,
    billing_rate AS BillingRate,
    cost_rate AS CostRate,
    line_amount AS LineAmount,
    line_cost_amount AS LineCostAmount
FROM doc_ab_timesheet__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<AgencyBillingTimesheetLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<AgencyBillingSalesInvoiceHead> ReadSalesInvoiceHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    due_date AS DueDate,
    client_id AS ClientId,
    project_id AS ProjectId,
    contract_id AS ContractId,
    currency_code AS CurrencyCode,
    memo AS Memo,
    amount AS Amount,
    notes AS Notes
FROM doc_ab_sales_invoice
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<AgencyBillingSalesInvoiceHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AgencyBillingSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    service_item_id AS ServiceItemId,
    source_timesheet_id AS SourceTimesheetId,
    description AS Description,
    quantity_hours AS QuantityHours,
    rate AS Rate,
    line_amount AS LineAmount
FROM doc_ab_sales_invoice__lines
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<AgencyBillingSalesInvoiceLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<AgencyBillingCustomerPaymentHead> ReadCustomerPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    document_date_utc AS DocumentDateUtc,
    client_id AS ClientId,
    cash_account_id AS CashAccountId,
    reference_number AS ReferenceNumber,
    amount AS Amount,
    notes AS Notes
FROM doc_ab_customer_payment
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<AgencyBillingCustomerPaymentHead>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AgencyBillingCustomerPaymentApply>> ReadCustomerPaymentAppliesAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    ordinal AS Ordinal,
    sales_invoice_id AS SalesInvoiceId,
    applied_amount AS AppliedAmount
FROM doc_ab_customer_payment__applies
WHERE document_id = @document_id
ORDER BY ordinal;
""";

        var rows = await uow.Connection.QueryAsync<AgencyBillingCustomerPaymentApply>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }
}
