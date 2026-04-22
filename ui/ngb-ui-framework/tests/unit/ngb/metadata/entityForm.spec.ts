import { describe, expect, it } from 'vitest'

import {
  buildFieldsPayload,
  defaultFindDisplayField,
  defaultIsFieldReadonly,
  ensureModelKeys,
  flattenFormFields,
} from '../../../../src/ngb/metadata/entityForm'

const form = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'display',
              label: 'Display',
              dataType: 'String',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
            },
            {
              key: 'is_active',
              label: 'Active',
              dataType: 'Boolean',
              uiControl: 5,
              isRequired: false,
              isReadOnly: false,
            },
            {
              key: 'amount',
              label: 'Amount',
              dataType: 'Money',
              uiControl: 4,
              isRequired: false,
              isReadOnly: false,
            },
            {
              key: 'customer_id',
              label: 'Customer',
              dataType: 'Guid',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

describe('metadata entity form helpers', () => {
  it('flattens form fields and finds the display field', () => {
    expect(flattenFormFields(form).map((field) => field.key)).toEqual([
      'display',
      'is_active',
      'amount',
      'customer_id',
    ])
    expect(defaultFindDisplayField(form)?.key).toBe('display')
  })

  it('ensures missing model keys using boolean and nullable defaults', () => {
    const model: Record<string, unknown> = {
      display: 'Invoice INV-001',
    }

    ensureModelKeys(form, model)

    expect(model).toEqual({
      display: 'Invoice INV-001',
      is_active: false,
      amount: null,
      customer_id: null,
    })
  })

  it('builds payload fields with normalized booleans, numbers, and reference ids', () => {
    const model = {
      display: '',
      is_active: 'yes',
      amount: '1250.50',
      customer_id: {
        id: '11111111-1111-1111-1111-111111111111',
        display: 'Riverfront Tower',
      },
    }

    expect(buildFieldsPayload(form, model)).toEqual({
      display: null,
      is_active: true,
      amount: 1250.5,
      customer_id: '11111111-1111-1111-1111-111111111111',
    })
  })

  it('respects force and status-based readonly rules', () => {
    expect(defaultIsFieldReadonly({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: {
        key: 'number',
        label: 'Number',
        dataType: 'String',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
        readOnlyWhenStatusIn: [2],
      },
      status: 2,
      forceReadonly: false,
    })).toBe(true)

    expect(defaultIsFieldReadonly({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: {
        key: 'memo',
        label: 'Memo',
        dataType: 'String',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
      },
      status: 1,
      forceReadonly: true,
    })).toBe(true)
  })
})
