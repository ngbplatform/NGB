export function catalogCollectionTitle(catalogType: string, displayName: string): string {
  switch (catalogType) {
    case 'pm.accounting_policy': return 'Accounting Policy'
    case 'pm.bank_account': return 'Bank Accounts'
    case 'pm.party': return 'Parties'
    case 'pm.property': return 'Properties & Units'
    case 'pm.receivable_charge_type': return 'Receivable Charge Types'
    case 'pm.payable_charge_type': return 'Payable Charge Types'
    case 'pm.maintenance_category': return 'Categories'
    default: return displayName
  }
}

export function documentCollectionTitle(documentType: string, displayName: string): string {
  switch (documentType) {
    case 'pm.lease': return 'Leases'
    case 'pm.maintenance_request': return 'Requests'
    case 'pm.work_order': return 'Work Orders'
    case 'pm.work_order_completion': return 'Completions'
    case 'pm.rent_charge': return 'Rent Charges'
    case 'pm.receivable_charge': return 'Other Charges'
    case 'pm.late_fee_charge': return 'Late Fees'
    case 'pm.receivable_payment': return 'Payments'
    case 'pm.receivable_returned_payment': return 'Returned Payments'
    case 'pm.receivable_credit_memo': return 'Credit Memos'
    case 'pm.receivable_apply': return 'Allocations'
    case 'pm.payable_charge': return 'Charges'
    case 'pm.payable_payment': return 'Payments'
    case 'pm.payable_credit_memo': return 'Credit Memos'
    case 'pm.payable_apply': return 'Allocations'
    default: return displayName
  }
}
