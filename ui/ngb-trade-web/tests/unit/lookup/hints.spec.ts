import { describe, expect, it, vi } from 'vitest'

vi.mock('ngb-ui-framework', () => ({
  lookupHintFromSource: (lookup?: unknown | null) => lookup ?? null,
}))

import { getTradeLookupHint } from '../../../src/lookup/hints'

describe('trade lookup hints', () => {
  it('preserves explicit catalog lookup metadata', () => {
    expect(getTradeLookupHint('trd.item', 'unit_of_measure_id', { kind: 'catalog', catalogType: 'trd.unit_of_measure' })).toEqual({
      kind: 'catalog',
      catalogType: 'trd.unit_of_measure',
    })
  })

  it('preserves explicit chart-of-accounts lookup metadata', () => {
    expect(getTradeLookupHint('trd.accounting_policy', 'cash_account_id', { kind: 'coa' })).toEqual({ kind: 'coa' })
  })

  it.each([
    'account_id',
    'cash_account_id',
    'ar_account_id',
    'inventory_adjustment_account_id',
    'COGS_ACCOUNT_ID',
  ])('infers chart-of-accounts lookups for account fields: %s', (fieldKey) => {
    expect(getTradeLookupHint('trd.accounting_policy', fieldKey)).toEqual({ kind: 'coa' })
  })

  it('trims field names before inference', () => {
    expect(getTradeLookupHint('trd.accounting_policy', '  cash_account_id  ')).toEqual({ kind: 'coa' })
  })

  it('returns null for non-account fields without explicit lookup metadata', () => {
    expect(getTradeLookupHint('trd.sales_invoice', 'customer_id')).toBeNull()
  })
})
