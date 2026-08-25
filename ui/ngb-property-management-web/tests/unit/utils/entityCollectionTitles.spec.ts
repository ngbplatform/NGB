import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('property-management collection titles', () => {
  it.each([
    ['pm.accounting_policy', 'Accounting Policy'], ['pm.bank_account', 'Bank Accounts'], ['pm.party', 'Parties'],
    ['pm.property', 'Properties & Units'], ['pm.receivable_charge_type', 'Receivable Charge Types'],
    ['pm.payable_charge_type', 'Payable Charge Types'], ['pm.maintenance_category', 'Categories'], ['unknown', 'Fallback'],
  ])('maps catalog %s', (typeCode, expected) => {
    expect(catalogCollectionTitle(typeCode, 'Fallback')).toBe(expected)
  })

  it.each([
    ['pm.lease', 'Leases'], ['pm.maintenance_request', 'Requests'], ['pm.work_order', 'Work Orders'],
    ['pm.work_order_completion', 'Completions'], ['pm.rent_charge', 'Rent Charges'],
    ['pm.receivable_charge', 'Other Charges'], ['pm.late_fee_charge', 'Late Fees'],
    ['pm.receivable_payment', 'Payments'], ['pm.receivable_returned_payment', 'Returned Payments'],
    ['pm.receivable_credit_memo', 'Credit Memos'], ['pm.receivable_apply', 'Allocations'],
    ['pm.payable_charge', 'Charges'], ['pm.payable_payment', 'Payments'],
    ['pm.payable_credit_memo', 'Credit Memos'], ['pm.payable_apply', 'Allocations'], ['unknown', 'Fallback'],
  ])('maps document %s', (typeCode, expected) => {
    expect(documentCollectionTitle(typeCode, 'Fallback')).toBe(expected)
  })
})
