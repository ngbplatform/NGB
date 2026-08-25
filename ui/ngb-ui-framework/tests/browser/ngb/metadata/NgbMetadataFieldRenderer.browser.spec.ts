import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import {
  StubCheckbox,
  StubDatePicker,
  StubInput,
  StubLookupControl,
  StubSelect,
} from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbCheckbox.vue', () => ({
  default: StubCheckbox,
}))

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', () => ({
  default: StubDatePicker,
}))

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: StubSelect,
}))

vi.mock('../../../../src/ngb/metadata/NgbMetadataLookupControl.vue', () => ({
  default: StubLookupControl,
}))

import NgbMetadataFieldRenderer from '../../../../src/ngb/metadata/NgbMetadataFieldRenderer.vue'

const SelectHarness = defineComponent({
  setup() {
    const value = ref('open')

    return () => h('div', [
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'status',
          label: 'Status',
          dataType: 'String',
          uiControl: 1,
          isRequired: false,
          isReadOnly: false,
          helpText: 'Choose a status',
        },
        model: {},
        modelValue: value.value,
        entityTypeCode: 'pm.invoice',
        behavior: {
          resolveFieldOptions: () => [
            { value: 'open', label: 'Open' },
            { value: 'posted', label: 'Posted' },
          ],
        },
        'onUpdate:modelValue': (next: unknown) => {
          value.value = String(next)
        },
      }),
      h('div', `value:${value.value}`),
    ])
  },
})

const MixedRendererHarness = defineComponent({
  setup() {
    const checkboxValue = ref(false)
    const lookupValue = ref(null)
    const dateValue = ref<string | null>(null)

    return () => h('div', [
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'customer_id',
          label: 'Customer',
          dataType: 'Guid',
          uiControl: 1,
          isRequired: false,
          isReadOnly: false,
          lookup: {
            kind: 'catalog',
            catalogType: 'pm.property',
          },
        },
        model: {},
        modelValue: lookupValue.value,
        entityTypeCode: 'pm.invoice',
        'onUpdate:modelValue': (next: unknown) => {
          lookupValue.value = next as object
        },
      }),
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'is_active',
          label: 'Active',
          dataType: 'Boolean',
          uiControl: 5,
          isRequired: false,
          isReadOnly: false,
        },
        model: {},
        modelValue: checkboxValue.value,
        entityTypeCode: 'pm.invoice',
        'onUpdate:modelValue': (next: unknown) => {
          checkboxValue.value = Boolean(next)
        },
      }),
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'document_date',
          label: 'Document date',
          dataType: 'Date',
          uiControl: 6,
          isRequired: false,
          isReadOnly: false,
        },
        model: {},
        modelValue: dateValue.value,
        entityTypeCode: 'pm.invoice',
        'onUpdate:modelValue': (next: unknown) => {
          dateValue.value = String(next)
        },
      }),
      h('div', `state-checkbox:${String(checkboxValue.value)}`),
      h('div', `state-lookup:${JSON.stringify(lookupValue.value)}`),
      h('div', `state-date:${String(dateValue.value)}`),
    ])
  },
})

const TextareaAndReferenceHarness = defineComponent({
  setup() {
    return () => h('div', [
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'memo',
          label: 'Memo',
          dataType: 'String',
          uiControl: 2,
          isRequired: false,
          isReadOnly: false,
          helpText: 'Internal note',
        },
        model: {},
        modelValue: 'Draft note',
        entityTypeCode: 'pm.invoice',
      }),
      h(NgbMetadataFieldRenderer, {
        field: {
          key: 'property',
          label: 'Property',
          dataType: 'String',
          uiControl: 1,
          isRequired: false,
          isReadOnly: false,
        },
        model: {},
        modelValue: {
          id: '11111111-1111-1111-1111-111111111111',
          display: 'Riverfront Tower',
        },
        entityTypeCode: 'pm.invoice',
      }),
    ])
  },
})

const DateTimeHarness = defineComponent({
  setup() {
    return () => h(NgbMetadataFieldRenderer, {
      field: {
        key: 'due_at_utc',
        label: 'Due At',
        dataType: 'DateTime',
        uiControl: 7,
        isRequired: false,
        isReadOnly: false,
      },
      model: {},
      modelValue: '2026-08-01T15:58:00.000Z',
      entityTypeCode: 'crm.activity_log',
    })
  },
})

const BoundaryValuesHarness = defineComponent({
  setup() {
    const memo = ref<unknown>(null)
    const baseField = {
      label: 'Boundary',
      dataType: 'String',
      uiControl: 1,
      isRequired: false,
      isReadOnly: false,
    }

    return () => h('div', [
      h(NgbMetadataFieldRenderer, {
        field: {
          ...baseField,
          key: 'integer_option',
          dataType: 'Int32',
          options: [
            { value: 1.9, label: 'Number' },
            { value: '2', label: 'Numeric string' },
            { value: 'bad', label: 'Invalid string' },
            { value: true, label: 'Boolean' },
            { value: { raw: 3 }, label: 'Object' },
            { value: null, label: 'Null' },
          ] as never,
        },
        model: {},
        modelValue: null,
        entityTypeCode: 'pm.invoice',
      }),
      h(NgbMetadataFieldRenderer, {
        field: {
          ...baseField,
          key: 'symbol_option',
          options: [{ value: Symbol('value'), label: 'Symbol' }] as never,
        },
        model: {},
        modelValue: Symbol('model'),
        entityTypeCode: 'pm.invoice',
      }),
      ...[
        ['null_input', null],
        ['number_input', 42],
        ['boolean_input', true],
        ['object_input', { raw: true }],
        ['symbol_input', Symbol('input')],
      ].map(([key, modelValue]) => h(NgbMetadataFieldRenderer, {
        field: { ...baseField, key: String(key) },
        model: {},
        modelValue,
        entityTypeCode: 'pm.invoice',
        disabled: key === 'boolean_input',
        readonly: key === 'object_input',
      })),
      h(NgbMetadataFieldRenderer, {
        field: { ...baseField, key: 'empty_memo', uiControl: 2 },
        model: {},
        modelValue: memo.value,
        entityTypeCode: 'pm.invoice',
        'onUpdate:modelValue': (value: unknown) => { memo.value = value },
      }),
      h('div', { 'data-testid': 'memo-state' }, String(memo.value ?? 'empty')),
    ])
  },
})

test('renders the select branch and propagates updates through the renderer', async () => {
  const view = await render(SelectHarness)

  await expect.element(view.getByTestId('stub-select')).toBeVisible()
  await expect.element(view.getByText('Choose a status')).toBeVisible()

  await view.getByTestId('stub-select').click()
  await expect.element(view.getByText('value:selected')).toBeVisible()
})

test('renders lookup, checkbox, and date branches and forwards updates', async () => {
  const view = await render(MixedRendererHarness)

  await expect.element(view.getByTestId('stub-lookup')).toBeVisible()
  await expect.element(view.getByTestId('stub-checkbox')).toBeVisible()
  await expect.element(view.getByTestId('stub-date-picker')).toBeVisible()

  await view.getByTestId('stub-lookup').click()
  await view.getByTestId('stub-checkbox').click()
  await view.getByTestId('stub-date-picker').click()

  await expect.element(view.getByText('state-checkbox:true')).toBeVisible()
  await expect.element(view.getByText('state-lookup:{"id":"lookup-id","display":"Lookup Label"}')).toBeVisible()
  await expect.element(view.getByText('state-date:2026-04-08')).toBeVisible()
})

test('renders textarea and reference-display branches', async () => {
  const view = await render(TextareaAndReferenceHarness)

  await expect.element(view.getByRole('textbox')).toBeVisible()
  await expect.element(view.getByText('Internal note')).toBeVisible()
  await expect.element(view.getByText('input:text:Riverfront Tower')).toBeVisible()
})

test('renders UTC instants as datetime-local values', async () => {
  const view = await render(DateTimeHarness)
  const expectedLocalValue = (() => {
    const date = new Date('2026-08-01T15:58:00.000Z')
    const pad = (value: number) => String(value).padStart(2, '0')
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
  })()

  await expect.element(view.getByText(`input:datetime-local:${expectedLocalValue}`)).toBeVisible()
})

test('normalizes every supported and unsupported control value at renderer boundaries', async () => {
  const view = await render(BoundaryValuesHarness)

  await expect.element(view.getByText('select:Number|Numeric string|Invalid string|Boolean|Object|Null')).toBeVisible()
  await expect.element(view.getByText('select:Symbol')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'input:text:', exact: true })).toBeVisible()
  await expect.element(view.getByText('input:text:42')).toBeVisible()
  await expect.element(view.getByText('input:text:true')).toBeVisible()
  await expect.element(view.getByText('input:text:[object Object]')).toBeVisible()
  await expect.element(view.getByText('input:text:Symbol(input)')).toBeVisible()

  const textarea = view.getByRole('textbox').last()
  await expect.element(textarea).toHaveValue('')
  await textarea.fill('Updated memo')
  await expect.element(view.getByTestId('memo-state')).toHaveTextContent('Updated memo')
})
