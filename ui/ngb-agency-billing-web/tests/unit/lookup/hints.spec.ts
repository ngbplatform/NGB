import { describe, expect, it, vi } from 'vitest'

vi.mock('ngb-ui-framework', () => ({
  lookupHintFromSource: (lookup?: unknown | null) => lookup ?? null,
}))

import { getAgencyBillingLookupHint } from '../../../src/lookup/hints'

describe('agency billing lookup hints', () => {
  it('preserves explicit metadata lookups from definitions', () => {
    expect(getAgencyBillingLookupHint('ab.project', 'client_id', { kind: 'catalog', catalogType: 'ab.client' })).toEqual({
      kind: 'catalog',
      catalogType: 'ab.client',
    })

    expect(getAgencyBillingLookupHint('ab.sales_invoice', 'source_timesheet_id', {
      kind: 'document',
      documentTypes: ['ab.timesheet'],
    })).toEqual({
      kind: 'document',
      documentTypes: ['ab.timesheet'],
    })
  })

  it.each([
    'account_id',
    'cash_account_id',
    'ar_account_id',
    'service_revenue_account_id',
    'DEFAULT_ACCOUNT_ID',
  ])('infers chart-of-accounts lookups for account-like field %s', (fieldKey) => {
    expect(getAgencyBillingLookupHint('ab.accounting_policy', fieldKey)).toEqual({ kind: 'coa' })
  })

  it('maps direct agency billing references to catalog and document targets', () => {
    expect(getAgencyBillingLookupHint('ab.project', 'client_id')).toEqual({ kind: 'catalog', catalogType: 'ab.client' })
    expect(getAgencyBillingLookupHint('ab.sales_invoice', 'service_item_id')).toEqual({ kind: 'catalog', catalogType: 'ab.service_item' })
    expect(getAgencyBillingLookupHint('ab.sales_invoice', 'source_timesheet_id')).toEqual({ kind: 'document', documentTypes: ['ab.timesheet'] })
    expect(getAgencyBillingLookupHint('ab.customer_payment', 'sales_invoice_id')).toEqual({ kind: 'document', documentTypes: ['ab.sales_invoice'] })
  })

  it('trims field names and returns null when no hint can be inferred', () => {
    expect(getAgencyBillingLookupHint('ab.accounting_policy', '  cash_account_id  ')).toEqual({ kind: 'coa' })
    expect(getAgencyBillingLookupHint('ab.client', 'notes')).toBeNull()
  })
})
