import { describe, expect, it, vi } from 'vitest'

const { lookupHintFromSource } = vi.hoisted(() => ({
  lookupHintFromSource: vi.fn((lookup?: unknown | null) => lookup ?? null),
}))

vi.mock('@ngbplatform/ui', () => ({ lookupHintFromSource }))

import { getCRMLookupHint } from '../../../src/lookup/hints'

describe('CRM lookup hints', () => {
  it('preserves an explicit metadata lookup regardless of entity and field casing', () => {
    const lookup = { kind: 'catalog', catalogType: 'crm.account' }

    expect(getCRMLookupHint('CRM.CONTACT', 'ACCOUNT_ID', lookup)).toBe(lookup)
    expect(lookupHintFromSource).toHaveBeenCalledWith(lookup)
  })

  it.each([undefined, null])('returns no hint when metadata lookup is %s', (lookup) => {
    expect(getCRMLookupHint('crm.contact', 'notes', lookup)).toBeNull()
  })
})
