import { describe, expect, it } from 'vitest'
import type { FieldMetadata } from '@ngbplatform/ui'

import { findDisplayField, isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'

function field(key: string, overrides: Partial<FieldMetadata> = {}): FieldMetadata {
  return {
    key,
    label: key,
    dataType: 'String',
    uiControl: 1,
    isRequired: false,
    isReadOnly: false,
    lookup: null,
    validation: null,
    helpText: null,
    ...overrides,
  }
}

describe('CRM metadata form behavior', () => {
  it('hides system display and number fields for documents', () => {
    expect(isFieldHidden({ entityTypeCode: 'crm.lead_intake', field: field('display'), isDocumentEntity: true })).toBe(true)
    expect(isFieldHidden({ entityTypeCode: 'crm.lead_intake', field: field('number'), isDocumentEntity: true })).toBe(true)
  })

  it('keeps quote amount hidden and readonly because it is derived from lines', () => {
    const amount = field('amount', { dataType: 'Decimal' })

    expect(isFieldHidden({ entityTypeCode: 'crm.quote', field: amount, isDocumentEntity: true })).toBe(true)
    expect(isFieldReadonly({ entityTypeCode: 'crm.quote', field: amount })).toBe(true)
  })

  it('marks CRM catalog display fields as readonly', () => {
    expect(isFieldReadonly({ entityTypeCode: 'crm.account', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ entityTypeCode: 'crm.contact', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ entityTypeCode: 'crm.product', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ entityTypeCode: 'crm.opportunity_stage', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ entityTypeCode: 'crm.unknown', field: field('display') })).toBe(false)
    expect(isFieldReadonly({ entityTypeCode: 'crm.account', field: field('name') })).toBe(false)
  })

  it('honors explicit, forced, and status-driven readonly metadata', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'crm.account',
      field: field('name'),
      forceReadonly: true,
    })).toBe(true)
    expect(isFieldReadonly({
      entityTypeCode: 'crm.account',
      field: field('name', { isReadOnly: true }),
    })).toBe(true)
    expect(isFieldReadonly({
      entityTypeCode: 'crm.account',
      field: field('name', { readOnlyWhenStatusIn: [2] }),
      status: 2,
    })).toBe(true)
    expect(isFieldReadonly({
      entityTypeCode: 'crm.account',
      field: field('name', { readOnlyWhenStatusIn: [2] }),
      status: 1,
    })).toBe(false)
    expect(isFieldReadonly({
      entityTypeCode: 'crm.account',
      field: field('name', { readOnlyWhenStatusIn: [2] }),
    })).toBe(false)
  })

  it('keeps structural and computed fields visible outside their document context', () => {
    expect(isFieldHidden({ entityTypeCode: 'crm.lead_intake', field: field('display'), isDocumentEntity: false })).toBe(false)
    expect(isFieldHidden({ entityTypeCode: 'crm.lead_intake', field: field('notes'), isDocumentEntity: true })).toBe(false)
    expect(isFieldHidden({ entityTypeCode: 'crm.account', field: field('amount'), isDocumentEntity: true })).toBe(false)
  })

  it('finds the first display field across nullable form containers', () => {
    const display = field('display')
    expect(findDisplayField({
      sections: [
        { title: 'Empty', rows: null },
        { title: 'Nullable', rows: [{ fields: [null, field('name')] }] },
        { title: 'Target', rows: [{ fields: [display, field('display')] }] },
      ],
    })).toBe(display)

    expect(findDisplayField({ sections: null })).toBeNull()
    expect(findDisplayField({ sections: [{ title: 'No fields', rows: [{ fields: null }] }] })).toBeNull()
  })
})
