import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  asTrimmedString: (value: unknown) => typeof value === 'string' ? value.trim() || null : null,
}))

import {
  findDisplayField,
  getPmPropertyKind,
  isFieldHidden,
  isFieldReadonly,
  resolveFieldOptions,
} from '../../../src/metadata/formBehavior'

const field = (key: string, overrides: Record<string, unknown> = {}) => ({
  key, label: key, dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false, ...overrides,
}) as never

describe('property-management form behavior', () => {
  it('resolves configured options and property kinds', () => {
    expect(resolveFieldOptions('pm.property', 'kind')).toHaveLength(2)
    expect(resolveFieldOptions('pm.maintenance_request', 'priority')).toHaveLength(4)
    expect(resolveFieldOptions('pm.work_order', 'cost_responsibility')).toHaveLength(4)
    expect(resolveFieldOptions('pm.work_order_completion', 'outcome')).toHaveLength(3)
    expect(resolveFieldOptions('pm.property', 'missing')).toBeNull()
    expect(getPmPropertyKind('other', { kind: 'Building' })).toBeNull()
    expect(getPmPropertyKind('pm.property', { kind: ' Building ' })).toBe('Building')
    expect(getPmPropertyKind('pm.property', { kind: 'Unit' })).toBe('Unit')
    expect(getPmPropertyKind('pm.property', { kind: 'Other' })).toBeNull()
  })

  it('covers forced, metadata, status, computed, kind, and editable readonly decisions', () => {
    const base = { entityTypeCode: 'other', model: {}, field: field('memo') }
    expect(isFieldReadonly({ ...base, forceReadonly: true })).toBe(true)
    expect(isFieldReadonly({ ...base, field: field('memo', { isReadOnly: true }) })).toBe(true)
    expect(isFieldReadonly({ ...base, field: field('display'), status: 0 })).toBe(true)
    expect(isFieldReadonly({ ...base, field: field('number'), status: 1 })).toBe(true)
    expect(isFieldReadonly({ ...base, entityTypeCode: 'pm.property', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ ...base, entityTypeCode: 'pm.bank_account', field: field('display') })).toBe(true)
    expect(isFieldReadonly({ ...base, entityTypeCode: 'pm.property', model: { kind: 'Building' }, field: field('kind') })).toBe(true)
    expect(isFieldReadonly({ ...base, entityTypeCode: 'pm.property', model: { kind: 'Unit' }, field: field('kind') })).toBe(true)
    expect(isFieldReadonly({ ...base, entityTypeCode: 'pm.property', model: { kind: 'Other' }, field: field('kind') })).toBe(false)
    expect(isFieldReadonly({ ...base, field: field('memo', { readOnlyWhenStatusIn: [2] }), status: 2 })).toBe(true)
    expect(isFieldReadonly({ ...base, field: field('memo', { readOnlyWhenStatusIn: [2] }), status: 1 })).toBe(false)
    expect(isFieldReadonly(base)).toBe(false)
  })

  it('covers document, non-property, unresolved, building, and unit visibility', () => {
    const check = (entityTypeCode: string, model: Record<string, unknown>, key: string, isDocumentEntity = false) =>
      isFieldHidden({ entityTypeCode, model, field: field(key), isDocumentEntity })
    expect(check('pm.lease', {}, 'display', true)).toBe(true)
    expect(check('pm.lease', {}, 'number', true)).toBe(true)
    expect(check('pm.lease', {}, 'memo', true)).toBe(false)
    expect(check('other', {}, 'display')).toBe(false)
    expect(check('pm.property', { kind: 'Building' }, 'kind')).toBe(true)
    expect(check('pm.property', {}, 'kind')).toBe(false)
    expect(check('pm.property', {}, 'city')).toBe(true)
    expect(check('pm.property', {}, 'unit_no')).toBe(true)
    expect(check('pm.property', {}, 'display')).toBe(false)
    expect(check('pm.property', { kind: 'Building' }, 'unit_no')).toBe(true)
    expect(check('pm.property', { kind: 'Building' }, 'city')).toBe(false)
    expect(check('pm.property', { kind: 'Unit' }, 'city')).toBe(true)
    expect(check('pm.property', { kind: 'Unit' }, 'unit_no')).toBe(false)
  })

  it('finds display fields through missing and populated form levels', () => {
    expect(findDisplayField({} as never)).toBeNull()
    expect(findDisplayField({ sections: [{}] } as never)).toBeNull()
    expect(findDisplayField({ sections: [{ rows: [{}] }] } as never)).toBeNull()
    expect(findDisplayField({ sections: [{ rows: [{ fields: [null, field('memo'), field('display')] }] }] } as never))
      .toMatchObject({ key: 'display' })
    expect(findDisplayField({ sections: [{ rows: [{ fields: [field('memo')] }] }] } as never)).toBeNull()
  })
})
