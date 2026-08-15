import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  asTrimmedString: (value: unknown) => typeof value === 'string' ? value.trim() || null : null,
  tryExtractReferenceDisplay: (value: unknown) => {
    if (typeof value === 'string') return value.trim() || null
    if (value && typeof value === 'object' && 'display' in value) {
      const display = (value as { display?: unknown }).display
      return typeof display === 'string' ? display.trim() || null : null
    }
    return null
  },
}))

import { PM_EDITOR_TAGS, resolvePmEditorEntityProfile } from '../../../src/editor/entityProfile'

function profile(kind: 'catalog' | 'document', typeCode: string) {
  return resolvePmEditorEntityProfile({ kind, typeCode } as never)!
}

function sync(kind: 'catalog' | 'document', typeCode: string, model: Record<string, unknown>) {
  profile(kind, typeCode).syncComputedDisplay?.({ model } as never)
  return model.display
}

describe('property-management entity profiles', () => {
  it('returns null for unsupported editor contexts', () => {
    expect(resolvePmEditorEntityProfile({ kind: 'catalog', typeCode: 'pm.unknown' } as never)).toBeNull()
    expect(resolvePmEditorEntityProfile({ kind: 'document', typeCode: 'pm.property' } as never)).toBeNull()
  })

  it('declares stable profile tags and watch policies', () => {
    expect(profile('catalog', 'pm.property')).toMatchObject({
      tags: [PM_EDITOR_TAGS.PROPERTY_CATALOG],
      sanitizeWatchFields: ['kind'],
      computedDisplayMode: 'always',
    })
    expect(profile('catalog', 'pm.bank_account')).toMatchObject({
      tags: [PM_EDITOR_TAGS.BANK_ACCOUNT_CATALOG],
      computedDisplayMode: 'always',
    })
    expect(profile('document', 'pm.lease')).toMatchObject({
      tags: [PM_EDITOR_TAGS.LEASE_DOCUMENT],
      computedDisplayMode: 'new_or_draft',
    })
  })

  it('sanitizes building, unit, and unknown property kinds', () => {
    const building = { kind: ' Building ', parent_property_id: 'parent', unit_no: '10' }
    profile('catalog', 'pm.property').sanitizeModelForEditing?.({ model: building } as never)
    expect(building).toMatchObject({ parent_property_id: null, unit_no: null })

    const unit = {
      kind: 'Unit',
      address_line1: 'one',
      address_line2: 'two',
      city: 'city',
      state: 'state',
      zip: 'zip',
    }
    profile('catalog', 'pm.property').sanitizeModelForEditing?.({ model: unit } as never)
    expect(unit).toMatchObject({
      address_line1: null,
      address_line2: null,
      city: null,
      state: null,
      zip: null,
    })

    const unknown = { kind: 'Other', city: 'Kept' }
    profile('catalog', 'pm.property').sanitizeModelForEditing?.({ model: unknown } as never)
    expect(unknown.city).toBe('Kept')
  })

  it.each([
    [{ kind: 'Building' }, undefined],
    [{ kind: 'Building', address_line1: '100 Main' }, '100 Main'],
    [{ kind: 'Building', address_line1: '100 Main', address_line2: 'Suite 1' }, '100 Main Suite 1'],
    [{ kind: 'Building', address_line1: '100 Main', city: 'Hoboken' }, '100 Main, Hoboken'],
    [{ kind: 'Building', address_line1: '100 Main', state: 'NJ', zip: '07030' }, '100 Main, NJ 07030'],
    [{ kind: 'Building', address_line1: '100 Main', state: 'NJ' }, '100 Main, NJ'],
    [{ kind: 'Building', address_line1: '100 Main', zip: '07030' }, '100 Main, 07030'],
    [{ kind: 'Other', address_line1: '100 Main' }, undefined],
  ])('computes building display %#', (model, expected) => {
    expect(sync('catalog', 'pm.property', model)).toBe(expected)
  })

  it.each([
    [{ kind: 'Unit', unit_no: '10' }, undefined],
    [{ kind: 'Unit', parent_property_id: { display: '100 Main' } }, undefined],
    [{ kind: 'Unit', parent_property_id: { display: '100 Main' }, unit_no: '10' }, '100 Main #10'],
  ])('computes unit display %#', (model, expected) => {
    expect(sync('catalog', 'pm.property', model)).toBe(expected)
  })

  it.each([
    [{}, undefined],
    [{ bank_name: 'Bank', account_name: 'Operating' }, undefined],
    [{ bank_name: 'Bank', account_name: 'Operating', last4: '1234' }, 'Bank Operating **** 1234'],
  ])('computes bank account display %#', (model, expected) => {
    expect(sync('catalog', 'pm.bank_account', model)).toBe(expected)
  })

  it.each([
    [{ property_id: { display: '100 Main' }, start_on_utc: 'bad' }, undefined],
    [{ start_on_utc: '2026-01-01' }, undefined],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-00-01' }, undefined],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-13-01' }, undefined],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-01-00' }, undefined],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-01-32' }, undefined],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-01-02' }, '100 Main — 01/02/2026 → Open'],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-01-02', end_on_utc: '2026-12-31' }, '100 Main — 01/02/2026 → 12/31/2026'],
    [{ property_id: { display: '100 Main' }, start_on_utc: '2026-01-02', end_on_utc: 'bad' }, '100 Main — 01/02/2026 → Open'],
  ])('computes lease display %#', (model, expected) => {
    expect(sync('document', 'pm.lease', model)).toBe(expected)
  })
})
