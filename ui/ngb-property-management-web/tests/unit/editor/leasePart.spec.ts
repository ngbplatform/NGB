import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  clonePlainData: (value: unknown) => JSON.parse(JSON.stringify(value)),
}))

import { useEntityEditorLeasePart } from '../../../src/editor/pm/useEntityEditorLeasePart'

function setup(isLease = true) {
  const lease = ref(isLease)
  return {
    lease,
    editor: useEntityEditorLeasePart({ isLeaseDocument: computed(() => lease.value) }),
  }
}

describe('property-management lease editor part', () => {
  it('provides the canonical default tenant row', () => {
    const { editor } = setup()
    expect(editor.defaultLeasePartyRow()).toEqual({
      party_id: null,
      role: 'PrimaryTenant',
      is_primary: true,
      ordinal: 1,
    })
  })

  it('clears rows for non-lease documents through both hydration paths', () => {
    const { editor } = setup(false)
    editor.leasePartiesRows.value = [{ party_id: 'party-1' }]
    editor.ensureLeasePartiesInitialized()
    expect(editor.leasePartiesRows.value).toEqual([])
    editor.leasePartiesRows.value = [{ party_id: 'party-1' }]
    editor.applyInitialParts({ parties: { rows: [{ party_id: 'party-2' }] } })
    expect(editor.leasePartiesRows.value).toEqual([])
    editor.leasePartiesRows.value = [{ party_id: 'party-1' }]
    editor.applyPersistedParts({ parties: { rows: [{ party_id: 'party-3' }] } })
    expect(editor.leasePartiesRows.value).toEqual([])
  })

  it('creates a default row when lease parts are empty or malformed', () => {
    const { editor } = setup()
    editor.applyInitialParts(null)
    expect(editor.leasePartiesRows.value).toEqual([expect.objectContaining({ ordinal: 1 })])
    editor.applyPersistedParts({ parties: { rows: null as never } })
    expect(editor.leasePartiesRows.value).toEqual([expect.objectContaining({ role: 'PrimaryTenant' })])
  })

  it('clones persisted rows and normalizes every ordinal', () => {
    const { editor } = setup()
    const rows = [
      { party_id: 'party-1', role: 'PrimaryTenant', is_primary: true, ordinal: 99 },
      { party_id: { id: 'party-2' }, role: 'Tenant', is_primary: false, ordinal: 99 },
    ]
    editor.applyPersistedParts({ parties: { rows } })
    expect(editor.leasePartiesRows.value.map((row) => row.ordinal)).toEqual([1, 2])
    expect(editor.leasePartiesRows.value).not.toBe(rows)
  })

  it('requires at least one tenant', () => {
    const { editor } = setup()
    expect(editor.validateLeasePartiesBeforeSave()).toBe('At least one tenant is required.')
  })

  it.each([
    [null, 'Tenant row #1: Party is required.'],
    ['', 'Tenant row #1: Party is required.'],
    [{}, 'Tenant row #1: Party is required.'],
    [{ id: 123 }, 'Tenant row #1: Party is required.'],
    [{ id: '' }, 'Tenant row #1: Party is required.'],
  ])('rejects an invalid party reference %#', (partyId, expected) => {
    const { editor } = setup()
    editor.leasePartiesRows.value = [{
      party_id: partyId,
      role: 'PrimaryTenant',
      is_primary: true,
      ordinal: 1,
    }]
    expect(editor.validateLeasePartiesBeforeSave()).toBe(expected)
  })

  it('accepts string and object party references', () => {
    const { editor } = setup()
    editor.leasePartiesRows.value = [
      { party_id: 'party-1', role: 'PrimaryTenant', is_primary: true, ordinal: 1 },
      { party_id: { id: 'party-2' }, role: 'Tenant', is_primary: false, ordinal: 2 },
    ]
    expect(editor.validateLeasePartiesBeforeSave()).toBeNull()
  })

  it('requires exactly one primary tenant', () => {
    const { editor } = setup()
    editor.leasePartiesRows.value = [
      { party_id: 'party-1', role: 'Tenant', is_primary: false, ordinal: 1 },
    ]
    expect(editor.validateLeasePartiesBeforeSave()).toBe('Exactly one tenant must be Primary.')
    editor.leasePartiesRows.value.push({
      party_id: 'party-2', role: 'PrimaryTenant', is_primary: true, ordinal: 2,
    })
    editor.leasePartiesRows.value[0]!.is_primary = true
    expect(editor.validateLeasePartiesBeforeSave()).toBe('Exactly one tenant must be Primary.')
  })

  it('requires the primary row to use the primary role', () => {
    const { editor } = setup()
    editor.leasePartiesRows.value = [
      { party_id: 'party-1', role: 'Tenant', is_primary: true, ordinal: 1 },
    ]
    expect(editor.validateLeasePartiesBeforeSave()).toBe("Primary tenant row must have role 'PrimaryTenant'.")
    editor.leasePartiesRows.value = [
      { party_id: 'party-1', is_primary: true, ordinal: 1 },
    ]
    expect(editor.validateLeasePartiesBeforeSave()).toBe("Primary tenant row must have role 'PrimaryTenant'.")
  })

  it('builds save parts only for leases and initializes rows first', () => {
    const { lease, editor } = setup(false)
    expect(editor.buildSaveParts()).toBeUndefined()
    lease.value = true
    expect(editor.buildSaveParts()).toEqual({
      parties: { rows: [expect.objectContaining({ role: 'PrimaryTenant', ordinal: 1 })] },
    })
  })

  it('builds an isolated copy payload only for leases', () => {
    const { lease, editor } = setup(false)
    expect(editor.buildCopyParts()).toBeNull()
    lease.value = true
    editor.leasePartiesRows.value = [
      { party_id: 'party-1', role: 'PrimaryTenant', is_primary: true, ordinal: 1 },
    ]
    const copy = editor.buildCopyParts()
    expect(copy).toEqual({ parties: { rows: editor.leasePartiesRows.value } })
    expect(copy!.parties!.rows).not.toBe(editor.leasePartiesRows.value)
  })
})
