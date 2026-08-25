import { describe, expect, it } from 'vitest'

import {
  buildFieldsPayload,
  defaultFindDisplayField,
  defaultIsFieldHidden,
  defaultIsFieldReadonly,
  ensureModelKeys,
  flattenFormFields,
  resolveMetadataFormBehavior,
  toDateTimeLocalInputValue,
  toUtcDateTimePayloadValue,
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

  it('converts datetime-local values to ISO UTC while preserving empty and invalid values', () => {
    const dateTimeForm = {
      sections: [{
        title: 'Schedule',
        rows: [{
          fields: [
            {
              key: 'due_at_utc',
              label: 'Due At',
              dataType: 'DateTime',
              uiControl: 7,
              isRequired: false,
              isReadOnly: false,
            },
            {
              key: 'completed_at_utc',
              label: 'Completed At',
              dataType: 'DateTime',
              uiControl: 7,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        }],
      }],
    }

    const localDueAt = '2026-08-01T11:58'
    expect(buildFieldsPayload(dateTimeForm, {
      due_at_utc: localDueAt,
      completed_at_utc: null,
    })).toEqual({
      due_at_utc: new Date(localDueAt).toISOString(),
      completed_at_utc: null,
    })

    expect(buildFieldsPayload(dateTimeForm, {
      due_at_utc: 'not-a-date',
      completed_at_utc: '2026-08-01T15:58:00Z',
    })).toEqual({
      due_at_utc: 'not-a-date',
      completed_at_utc: '2026-08-01T15:58:00.000Z',
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

  it('formats datetime values at minute, second, and millisecond precision', () => {
    const formatExpected = (input: string, includeSeconds: boolean, includeMilliseconds: boolean) => {
      const date = new Date(input)
      const pad = (value: number) => String(value).padStart(2, '0')
      const minute = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
      if (!includeSeconds) return minute
      const second = `${minute}:${pad(date.getSeconds())}`
      return includeMilliseconds ? `${second}.${String(date.getMilliseconds()).padStart(3, '0')}` : second
    }

    expect(toDateTimeLocalInputValue(null)).toBe('')
    expect(toDateTimeLocalInputValue(42)).toBe('42')
    expect(toDateTimeLocalInputValue('not-an-iso-date')).toBe('not-an-iso-date')
    expect(toDateTimeLocalInputValue('2026-99-99T25:61Z')).toBe('2026-99-99T25:61Z')
    expect(toDateTimeLocalInputValue('2026-08-01T15:58:00Z'))
      .toBe(formatExpected('2026-08-01T15:58:00Z', false, false))
    expect(toDateTimeLocalInputValue('2026-08-01T15:58:09Z'))
      .toBe(formatExpected('2026-08-01T15:58:09Z', true, false))
    expect(toDateTimeLocalInputValue('2026-08-01T15:58:09.025Z'))
      .toBe(formatExpected('2026-08-01T15:58:09.025Z', true, true))

    expect(toUtcDateTimePayloadValue(null)).toBeNull()
    expect(toUtcDateTimePayloadValue(42)).toBe(42)
    expect(toUtcDateTimePayloadValue('not-an-iso-date')).toBe('not-an-iso-date')
    expect(toUtcDateTimePayloadValue('2026-99-99T25:61Z')).toBe('2026-99-99T25:61Z')
    expect(toUtcDateTimePayloadValue('2026-08-01T15:58:09+02:00')).toBe('2026-08-01T13:58:09.000Z')
  })

  it('handles absent sections, rows, fields, and forms without a display field', () => {
    expect(flattenFormFields()).toEqual([])
    expect(flattenFormFields({})).toEqual([])
    expect(flattenFormFields({ sections: [{ title: 'No rows' }, { title: 'No fields', rows: [{}] }] })).toEqual([])
    expect(defaultFindDisplayField({ sections: [{ rows: [{ fields: [{
      key: 'code', label: 'Code', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false,
    }] }] }] })).toBeNull()
  })

  it('covers readonly and hidden defaults for every rule outcome', () => {
    const base = {
      entityTypeCode: 'pm.invoice',
      model: {},
      field: {
        key: 'memo', label: 'Memo', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false,
      },
      status: 1,
      forceReadonly: false,
    }

    expect(defaultIsFieldReadonly(base)).toBe(false)
    expect(defaultIsFieldReadonly({ ...base, field: { ...base.field, isReadOnly: true } })).toBe(true)
    expect(defaultIsFieldReadonly({ ...base, status: undefined })).toBe(false)
    expect(defaultIsFieldReadonly({ ...base, field: { ...base.field, readOnlyWhenStatusIn: [2] } })).toBe(false)
    expect(defaultIsFieldHidden({ entityTypeCode: 'pm.invoice', model: {}, field: base.field })).toBe(false)
  })

  it('normalizes every supported control and data type including invalid boundary values', () => {
    const fields = [
      { key: 'undefined_value', dataType: 'String', uiControl: 1 },
      { key: 'date_by_type', dataType: 'DateTime', uiControl: 1 },
      { key: 'checkbox_false', dataType: 'String', uiControl: 5 },
      { key: 'number_control_number', dataType: 'String', uiControl: 3 },
      { key: 'number_control_string', dataType: 'String', uiControl: 4 },
      { key: 'number_control_invalid', dataType: 'String', uiControl: 3 },
      { key: 'number_control_object', dataType: 'String', uiControl: 4 },
      { key: 'select_control', dataType: 'String', uiControl: 6 },
      { key: 'int_number', dataType: 'Int32', uiControl: 1 },
      { key: 'int_string', dataType: 'Int32', uiControl: 1 },
      { key: 'int_invalid', dataType: 'Int32', uiControl: 1 },
      { key: 'int_object', dataType: 'Int32', uiControl: 1 },
      { key: 'decimal_number', dataType: 'Decimal', uiControl: 1 },
      { key: 'decimal_string', dataType: 'Decimal', uiControl: 1 },
      { key: 'decimal_invalid', dataType: 'Decimal', uiControl: 1 },
      { key: 'decimal_object', dataType: 'Decimal', uiControl: 1 },
      { key: 'money_value', dataType: 'Money', uiControl: 1 },
      { key: 'boolean_by_type', dataType: 'Boolean', uiControl: 1 },
      { key: 'plain_value', dataType: 'String', uiControl: 1 },
    ].map((field) => ({
      ...field,
      label: field.key,
      isRequired: false,
      isReadOnly: false,
    }))
    const boundaryForm = { sections: [{ rows: [{ fields }] }] }
    const objectValue = { nested: true }

    const model: Record<string, unknown> = {
      date_by_type: '2026-08-01T15:58:09Z',
      checkbox_false: 0,
      number_control_number: 12.5,
      number_control_string: '13.5',
      number_control_invalid: 'not-a-number',
      number_control_object: objectValue,
      select_control: 'selected',
      int_number: 12.9,
      int_string: '13',
      int_invalid: 'invalid',
      int_object: objectValue,
      decimal_number: 14.5,
      decimal_string: '15.5',
      decimal_invalid: 'invalid',
      decimal_object: objectValue,
      money_value: '16.5',
      boolean_by_type: 'yes',
      plain_value: 'plain',
    }

    expect(buildFieldsPayload(boundaryForm, model)).toEqual({
      undefined_value: null,
      date_by_type: '2026-08-01T15:58:09.000Z',
      checkbox_false: false,
      number_control_number: 12.5,
      number_control_string: 13.5,
      number_control_invalid: 'not-a-number',
      number_control_object: objectValue,
      select_control: 'selected',
      int_number: 12,
      int_string: 13,
      int_invalid: 'invalid',
      int_object: objectValue,
      decimal_number: 14.5,
      decimal_string: 15.5,
      decimal_invalid: 'invalid',
      decimal_object: objectValue,
      money_value: 16.5,
      boolean_by_type: true,
      plain_value: 'plain',
    })

    ensureModelKeys(boundaryForm, model)
    expect(model.undefined_value).toBeNull()
  })

  it('resolves default and overridden metadata form behavior', () => {
    expect(resolveMetadataFormBehavior()).toEqual(expect.any(Object))
    const findDisplayField = () => null
    expect(resolveMetadataFormBehavior({ findDisplayField }).findDisplayField).toBe(findDisplayField)
  })
})
