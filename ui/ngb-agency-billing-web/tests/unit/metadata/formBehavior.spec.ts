import { describe, expect, it } from 'vitest'

import { findDisplayField, isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'

describe('agency billing form behavior', () => {
  it('marks computed document and catalog fields as readonly', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'ab.timesheet',
      model: {},
      field: { key: 'amount', label: 'Amount', dataType: 'Money', uiControl: 4, isRequired: false, isReadOnly: false },
    })).toBe(true)

    expect(isFieldReadonly({
      entityTypeCode: 'ab.timesheet',
      model: {},
      field: { key: 'cost_amount', label: 'Cost Amount', dataType: 'Money', uiControl: 4, isRequired: false, isReadOnly: false },
    })).toBe(true)

    expect(isFieldReadonly({
      entityTypeCode: 'ab.client',
      model: {},
      field: { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
    })).toBe(true)
  })

  it('respects forced readonly flags and status-based readonly metadata', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'ab.client_contract',
      model: {},
      field: { key: 'memo', label: 'Memo', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
      forceReadonly: true,
    })).toBe(true)

    expect(isFieldReadonly({
      entityTypeCode: 'ab.sales_invoice',
      model: {},
      field: {
        key: 'due_date',
        label: 'Due Date',
        dataType: 'Date',
        uiControl: 6,
        isRequired: false,
        isReadOnly: false,
        readOnlyWhenStatusIn: [2],
      },
      status: 2,
    })).toBe(true)
  })

  it('hides structural and computed fields from document and catalog forms', () => {
    expect(isFieldHidden({
      entityTypeCode: 'ab.sales_invoice',
      model: {},
      field: { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
      isDocumentEntity: true,
    })).toBe(true)

    expect(isFieldHidden({
      entityTypeCode: 'ab.timesheet',
      model: {},
      field: { key: 'cost_amount', label: 'Cost Amount', dataType: 'Money', uiControl: 4, isRequired: false, isReadOnly: false },
      isDocumentEntity: true,
    })).toBe(true)

    expect(isFieldHidden({
      entityTypeCode: 'ab.project',
      model: {},
      field: { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
      isDocumentEntity: false,
    })).toBe(true)
  })

  it('finds the display field anywhere in the form tree', () => {
    expect(findDisplayField({
      sections: [
        {
          title: 'Main',
          rows: [
            { fields: [{ key: 'client_code', label: 'Client Code', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false }] },
            { fields: [{ key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false }] },
          ],
        },
      ],
    })?.label).toBe('Display')
  })
})
