import { describe, expect, it } from 'vitest'

import { resolveFieldRendererState } from '../../../../src/ngb/metadata/fieldRendererState'

function createField(overrides: Partial<Parameters<typeof resolveFieldRendererState>[0]['field']> = {}) {
  return {
    key: 'memo',
    label: 'Memo',
    dataType: 'String',
    uiControl: 1,
    isRequired: false,
    isReadOnly: false,
    ...overrides,
  }
}

describe('metadata field renderer state', () => {
  it('prefers select and lookup modes when behavior options or lookup hints are available', () => {
    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField(),
      modelValue: 'open',
      behavior: {
        resolveFieldOptions: () => [
          { value: 'open', label: 'Open' },
          { value: 'posted', label: 'Posted' },
        ],
      },
    })).toEqual({
      mode: 'select',
      inputType: 'text',
      fieldOptions: [
        { value: 'open', label: 'Open' },
        { value: 'posted', label: 'Posted' },
      ],
      hint: null,
    })

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({
        key: 'customer_id',
        lookup: { kind: 'catalog', catalogType: 'pm.property' },
      }),
      modelValue: '11111111-1111-1111-1111-111111111111',
    })).toEqual({
      mode: 'lookup',
      inputType: 'text',
      fieldOptions: null,
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
    })
  })

  it('uses field metadata options when behavior does not override them', () => {
    expect(resolveFieldRendererState({
      entityTypeCode: 'ab.project',
      model: {},
      field: createField({
        key: 'status',
        dataType: 'Int32',
        options: [
          { value: '1', label: 'Planned' },
          { value: '2', label: 'Active' },
        ],
      }),
      modelValue: 2,
    })).toEqual({
      mode: 'select',
      inputType: 'text',
      fieldOptions: [
        { value: '1', label: 'Planned' },
        { value: '2', label: 'Active' },
      ],
      hint: null,
    })
  })

  it('derives checkbox, textarea, reference-display, date, and typed input modes', () => {
    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({ dataType: 'Boolean', uiControl: 5 }),
      modelValue: true,
    }).mode).toBe('checkbox')

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({ uiControl: 2 }),
      modelValue: 'Long note',
    }).mode).toBe('textarea')

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField(),
      modelValue: {
        id: '11111111-1111-1111-1111-111111111111',
        display: 'Riverfront Tower',
      },
    }).mode).toBe('reference-display')

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({ dataType: 'Date', uiControl: 6 }),
      modelValue: '2026-04-08',
    }).mode).toBe('date')

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({ dataType: 'DateTime', uiControl: 7 }),
      modelValue: '2026-04-08T12:30',
    })).toEqual({
      mode: 'input',
      inputType: 'datetime-local',
      fieldOptions: null,
      hint: null,
    })

    expect(resolveFieldRendererState({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: createField({ dataType: 'Money', uiControl: 4 }),
      modelValue: 1250,
    }).inputType).toBe('number')
  })
})
