import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  asTrimmedString: (value: unknown) => value == null ? null : String(value).trim() || null,
}))

import { resolveCRMEditorEntityProfile } from '../../../src/editor/entityProfile'

function sync(kind: 'catalog' | 'document', typeCode: string, model: Record<string, unknown>) {
  const profile = resolveCRMEditorEntityProfile({ kind, typeCode } as never)
  profile?.syncComputedDisplay?.({ model } as never)
  return { profile, display: model.display }
}

describe('CRM entity profiles', () => {
  it('returns null for unsupported catalog and document types', () => {
    expect(sync('catalog', 'crm.unknown', {}).profile).toBeNull()
    expect(sync('document', 'crm.unknown', {}).profile).toBeNull()
  })

  it.each([
    ['crm.account', {}, null],
    ['crm.account', { name: ' Acme ' }, 'Acme'],
    ['crm.contact', {}, null],
    ['crm.contact', { first_name: ' Avery ' }, 'Avery'],
    ['crm.contact', { last_name: ' Kim ' }, 'Kim'],
    ['crm.contact', { first_name: ' Avery ', last_name: ' Kim ' }, 'Avery Kim'],
    ['crm.product', {}, null],
    ['crm.product', { sku: ' SKU-1 ' }, 'SKU-1'],
    ['crm.product', { sku: 'SKU-1', name: ' Product ' }, 'Product'],
    ['crm.opportunity_stage', {}, null],
    ['crm.opportunity_stage', { ordinal: 10 }, '10'],
    ['crm.opportunity_stage', { ordinal: 10, name: ' Won ' }, 'Won'],
  ])('computes %s catalog display %#', (typeCode, model, expected) => {
    const result = sync('catalog', typeCode, model as Record<string, unknown>)
    expect(result.profile).toMatchObject({ computedDisplayMode: 'always' })
    expect(result.display).toBe(expected)
  })

  it.each([
    ['crm.lead_intake', 'Lead Intake'],
    ['crm.lead_qualification', 'Lead Qualification'],
    ['crm.lead_conversion', 'Lead Conversion'],
    ['crm.opportunity_update', 'Opportunity Update'],
    ['crm.quote', 'Quote'],
    ['crm.activity_log', 'Activity Log'],
  ])('computes %s display with its configured title', (typeCode, title) => {
    const result = sync('document', typeCode, {
      number: ' N-1 ',
      document_date_utc: '2026-07-31',
    })
    expect(result.profile).toMatchObject({
      computedDisplayWatchFields: ['number', 'document_date_utc'],
      computedDisplayMode: 'new_or_draft',
    })
    expect(result.display).toBe(`${title} N-1 7/31/2026`)
  })

  it.each([
    [{}, 'Lead Intake'],
    [{ number: 'N-1' }, 'Lead Intake N-1'],
    [{ document_date_utc: '2026-01-02' }, 'Lead Intake 1/2/2026'],
    [{ number: ' ', document_date_utc: 'not-a-date' }, 'Lead Intake'],
    [{ document_date_utc: 20260731 }, 'Lead Intake'],
  ])('handles optional document display parts %#', (model, expected) => {
    expect(sync('document', 'crm.lead_intake', model).display).toBe(expected)
  })
})
