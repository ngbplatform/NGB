import { describe, expect, it } from 'vitest'

import { resolveTradeEditorEntityProfile } from '../../../src/editor/entityProfile'

describe('trade editor entity profile', () => {
  it('syncs item display into the hidden name field', () => {
    const profile = resolveTradeEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'trd.item',
    } as never)

    const model = {
      display: 'Portable Fan 18 in',
      name: '',
    }

    profile?.syncComputedDisplay?.({ model } as never)

    expect(profile?.computedDisplayMode).toBe('always')
    expect(profile?.computedDisplayWatchFields).toEqual(['display'])
    expect(model.name).toBe('Portable Fan 18 in')
  })

  it('builds warehouse display from name and address', () => {
    const profile = resolveTradeEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'trd.warehouse',
    } as never)

    const model = {
      name: 'South Hub',
      address: '14 Logistics Way',
      display: '',
    }

    profile?.syncComputedDisplay?.({ model } as never)

    expect(model.display).toBe('South Hub — 14 Logistics Way')
  })

  it('formats document display from the number and date field', () => {
    const profile = resolveTradeEditorEntityProfile({
      kind: 'document',
      typeCode: 'trd.sales_invoice',
    } as never)

    const model = {
      number: 'SI-2048',
      document_date_utc: '2026-04-18',
      display: '',
    }

    profile?.syncComputedDisplay?.({ model } as never)

    expect(profile?.computedDisplayMode).toBe('new_or_draft')
    expect(profile?.computedDisplayWatchFields).toEqual(['number', 'document_date_utc'])
    expect(model.display).toBe('Sales Invoice SI-2048 4/18/2026')
  })

  it('returns null for unsupported trade editor contexts', () => {
    expect(resolveTradeEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'trd.price_type',
    } as never)).toBeNull()
  })
})
