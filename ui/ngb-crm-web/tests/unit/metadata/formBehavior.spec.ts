import { describe, expect, it } from 'vitest'
import type { FieldMetadata } from '@ngbplatform/ui'

import { isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'

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
    expect(isFieldReadonly({ entityTypeCode: 'crm.account', field: field('name') })).toBe(false)
  })
})
