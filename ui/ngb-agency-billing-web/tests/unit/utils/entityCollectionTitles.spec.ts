import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('agency billing entity collection titles', () => {
  it('maps known catalog titles and preserves unknown display names', () => {
    const cases = [
      ['ab.client', 'Clients'], ['ab.team_member', 'Team Members'], ['ab.project', 'Projects'],
      ['ab.rate_card', 'Rate Cards'], ['ab.service_item', 'Service Items'],
      ['ab.payment_terms', 'Payment Terms'], ['ab.accounting_policy', 'Accounting Policy'],
    ]
    for (const [typeCode, expected] of cases) expect(catalogCollectionTitle(typeCode!, 'Fallback')).toBe(expected)
    expect(catalogCollectionTitle('ab.unknown', 'Unknown')).toBe('Unknown')
  })

  it('maps known document titles and preserves unknown display names', () => {
    const cases = [
      ['ab.client_contract', 'Client Contracts'], ['ab.timesheet', 'Timesheets'],
      ['ab.sales_invoice', 'Sales Invoices'], ['ab.customer_payment', 'Customer Payments'],
    ]
    for (const [typeCode, expected] of cases) expect(documentCollectionTitle(typeCode!, 'Fallback')).toBe(expected)
    expect(documentCollectionTitle('ab.unknown', 'Unknown')).toBe('Unknown')
  })
})
