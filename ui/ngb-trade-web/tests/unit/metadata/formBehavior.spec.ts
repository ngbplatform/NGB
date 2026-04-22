import { describe, expect, it } from 'vitest'

import { findDisplayField, isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'

const baseField = {
  key: 'notes',
  label: 'Notes',
  dataType: 'String',
  uiControl: 1,
  isRequired: false,
  isReadOnly: false,
}

describe('trade metadata form behavior', () => {
  it('forces readonly when the editor is explicitly readonly', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.sales_invoice',
      field: baseField,
      forceReadonly: true,
    })).toBe(true)
  })

  it('honors field-level readonly metadata', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.sales_invoice',
      field: { ...baseField, isReadOnly: true },
    })).toBe(true)
  })

  it('treats warehouse display as computed readonly text', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.warehouse',
      field: { ...baseField, key: 'display' },
    })).toBe(true)
  })

  it.each([
    'trd.purchase_receipt',
    'trd.sales_invoice',
    'trd.inventory_adjustment',
    'trd.customer_return',
    'trd.vendor_return',
  ])('treats computed amount as readonly for %s', (entityTypeCode) => {
    expect(isFieldReadonly({
      entityTypeCode,
      field: { ...baseField, key: 'amount', dataType: 'Money' },
    })).toBe(true)
  })

  it('uses status-based readonly rules when present', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.sales_invoice',
      field: { ...baseField, readOnlyWhenStatusIn: [2, 3] },
      status: 2,
    })).toBe(true)
  })

  it('leaves ordinary editable fields writable', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.sales_invoice',
      field: baseField,
      status: 1,
    })).toBe(false)
  })

  it('hides document display field from document editors', () => {
    expect(isFieldHidden({
      entityTypeCode: 'trd.sales_invoice',
      field: { ...baseField, key: 'display' },
      isDocumentEntity: true,
    })).toBe(true)
  })

  it('hides document number field from document editors', () => {
    expect(isFieldHidden({
      entityTypeCode: 'trd.sales_invoice',
      field: { ...baseField, key: 'number' },
      isDocumentEntity: true,
    })).toBe(true)
  })

  it.each([
    'trd.purchase_receipt',
    'trd.sales_invoice',
    'trd.inventory_adjustment',
    'trd.customer_return',
    'trd.vendor_return',
  ])('hides computed amount field from document forms for %s', (entityTypeCode) => {
    expect(isFieldHidden({
      entityTypeCode,
      field: { ...baseField, key: 'amount', dataType: 'Money' },
      isDocumentEntity: true,
    })).toBe(true)
  })

  it.each([
    'trd.item',
    'trd.unit_of_measure',
    'trd.party',
  ])('hides redundant name field for %s', (entityTypeCode) => {
    expect(isFieldHidden({
      entityTypeCode,
      field: { ...baseField, key: 'name' },
      isDocumentEntity: false,
    })).toBe(true)
  })

  it('keeps ordinary catalog fields visible', () => {
    expect(isFieldHidden({
      entityTypeCode: 'trd.item',
      field: baseField,
      isDocumentEntity: false,
    })).toBe(false)
  })

  it('returns the first display field in section-row order', () => {
    expect(findDisplayField({
      sections: [
        {
          title: 'Primary',
          rows: [
            { fields: [{ ...baseField, key: 'name' }] },
            { fields: [{ ...baseField, key: 'display', label: 'Display' }] },
            { fields: [{ ...baseField, key: 'display', label: 'Display Again' }] },
          ],
        },
      ],
    })?.label).toBe('Display')
  })

  it('returns null when no display field exists', () => {
    expect(findDisplayField({
      sections: [{ title: 'Primary', rows: [{ fields: [{ ...baseField, key: 'name' }] }] }],
    })).toBeNull()
  })
})
