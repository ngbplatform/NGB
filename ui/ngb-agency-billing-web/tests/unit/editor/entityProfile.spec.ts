import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  asTrimmedString: (value: unknown) => value == null ? null : String(value).trim() || null,
}))

import { resolveAgencyBillingEditorEntityProfile } from '../../../src/editor/entityProfile'

function sync(typeCode: string, model: Record<string, unknown>) {
  const profile = resolveAgencyBillingEditorEntityProfile({ kind: 'catalog', typeCode } as never)
  profile?.syncComputedDisplay?.({ model } as never)
  return { profile, display: model.display }
}

describe('agency billing entity profiles', () => {
  it('returns null outside supported catalog types', () => {
    expect(resolveAgencyBillingEditorEntityProfile({ kind: 'document', typeCode: 'ab.timesheet' } as never)).toBeNull()
    expect(resolveAgencyBillingEditorEntityProfile({ kind: 'catalog', typeCode: 'ab.unknown' } as never)).toBeNull()
  })

  it.each([
    ['ab.client', 'name'],
    ['ab.project', 'name'],
    ['ab.service_item', 'name'],
    ['ab.payment_terms', 'name'],
    ['ab.team_member', 'full_name'],
  ])('syncs %s from %s', (typeCode, field) => {
    const model = { [field]: ' Display value ' }
    const result = sync(typeCode, model)
    expect(result.profile).toMatchObject({ computedDisplayMode: 'always' })
    expect(result.display).toBe('Display value')
  })

  it.each([
    [{}, null],
    [{ name: ' Standard ' }, 'Standard'],
    [{ service_title: ' Consulting ' }, 'Consulting'],
    [{ name: ' Standard ', service_title: ' Consulting ' }, 'Standard · Consulting'],
  ])('computes rate-card display %#', (model, expected) => {
    const result = sync('ab.rate_card', model)
    expect(result.profile?.computedDisplayWatchFields).toEqual(['name', 'service_title'])
    expect(result.display).toBe(expected)
  })
})
