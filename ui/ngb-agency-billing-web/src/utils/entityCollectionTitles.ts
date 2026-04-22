export function catalogCollectionTitle(catalogType: string, displayName: string): string {
  switch (catalogType) {
    case 'ab.client': return 'Clients'
    case 'ab.team_member': return 'Team Members'
    case 'ab.project': return 'Projects'
    case 'ab.rate_card': return 'Rate Cards'
    case 'ab.service_item': return 'Service Items'
    case 'ab.payment_terms': return 'Payment Terms'
    case 'ab.accounting_policy': return 'Accounting Policy'
    default: return displayName
  }
}

export function documentCollectionTitle(documentType: string, displayName: string): string {
  switch (documentType) {
    case 'ab.client_contract': return 'Client Contracts'
    case 'ab.timesheet': return 'Timesheets'
    case 'ab.sales_invoice': return 'Sales Invoices'
    case 'ab.customer_payment': return 'Customer Payments'
    default: return displayName
  }
}
