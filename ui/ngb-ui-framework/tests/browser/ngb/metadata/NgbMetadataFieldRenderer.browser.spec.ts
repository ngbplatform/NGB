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
