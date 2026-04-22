import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('agency billing entity collection titles', () => {
  it('maps known catalog titles and preserves unknown display names', () => {
    expect(catalogCollectionTitle('ab.client', 'Client')).toBe('Clients')
    expect(catalogCollectionTitle('ab.accounting_policy', 'Accounting Policy')).toBe('Accounting Policy')
    expect(catalogCollectionTitle('ab.unknown', 'Unknown')).toBe('Unknown')
  })

  it('maps known document titles and preserves unknown display names', () => {
    expect(documentCollectionTitle('ab.client_contract', 'Client Contract')).toBe('Client Contracts')
    expect(documentCollectionTitle('ab.customer_payment', 'Customer Payment')).toBe('Customer Payments')
    expect(documentCollectionTitle('ab.unknown', 'Unknown')).toBe('Unknown')
  })
})
