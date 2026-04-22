import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubLookup } from '../accounting/stubs'
import {
  StubInput,
  StubMultiSelect,
  StubSelect,
  StubSwitch,
} from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbLookup.vue', () => ({
  default: StubLookup,
}))

vi.mock('../../../../src/ngb/primitives/NgbMultiSelect.vue', () => ({
  default: StubMultiSelect,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: StubSelect,
}))

vi.mock('../../../../src/ngb/primitives/NgbSwitch.vue', () => ({
  default: StubSwitch,
}))

import NgbFilterFieldControl from '../../../../src/ngb/metadata/NgbFilterFieldControl.vue'

const LookupHarness = defineComponent({
  setup() {
    const state = ref({
      raw: '',
      items: [{ id: 'property-1', label: 'Riverfront Tower' }],
      includeDescendants: false,
    })
    const lookupItems = ref([
      { id: 'property-2', label: 'Harbor Tower' },
      { id: 'property-3', label: 'Market Square' },
    ])
    const queryLog = ref<string[]>([])
    const openCount = ref(0)

    return () => h('div', [
      h(NgbFilterFieldControl, {
        field: {
          label: 'Property',
          lookup: {
            kind: 'catalog',
            catalogType: 'pm.property',
          },
          supportsIncludeDescendants: true,
        },
        state: state.value,
        lookupItems: lookupItems.value,
        showOpen: true,
        showClear: true,
        allowIncludeDescendants: true,
        onLookupQuery: (query: string) => {
          queryLog.value = [...queryLog.value, query]
        },
        'onUpdate:items': (items: Array<{ id: string; label: string }>) => {
          state.value = { ...state.value, items }
        },
        'onUpdate:includeDescendants': (value: boolean) => {
          state.value = { ...state.value, includeDescendants: value }
        },
        onOpen: () => {
          openCount.value += 1
        },
      }),
      h('div', `query-log:${queryLog.value.join('|') || 'none'}`),
      h('div', `selected:${state.value.items.map((item) => item.label).join('|') || 'none'}`),
      h('div', `descendants:${String(state.value.includeDescendants)}`),
      h('div', `opens:${openCount.value}`),
    ])
  },
})

const MultiLookupHarness = defineComponent({
  setup() {
    const state = ref({
      raw: '',
      items: [],
      includeDescendants: false,
    })
    const queryLog = ref<string[]>([])

    return () => h('div', [
      h(NgbFilterFieldControl, {
        field: {
          label: 'Documents',
          isMulti: true,
          lookup: {
            kind: 'document',
            documentTypes: ['pm.invoice'],
          },
        },
        state: state.value,
        lookupItems: [
          { id: 'doc-1', label: 'Invoice INV-001' },
          { id: 'doc-2', label: 'Invoice INV-002' },
        ],
        onLookupQuery: (query: string) => {
          queryLog.value = [...queryLog.value, query]
        },
        'onUpdate:items': (items: Array<{ id: string; label: string }>) => {
          state.value = { ...state.value, items }
        },
      }),
      h('div', `query-log:${queryLog.value.join('|') || 'none'}`),
      h('div', `selected:${state.value.items.map((item) => item.label).join('|') || 'none'}`),
    ])
  },
})

const SelectHarness = defineComponent({
  setup() {
    const state = ref({
      raw: 'open',
      items: [],
      includeDescendants: false,
    })

    return () => h('div', [
      h(NgbFilterFieldControl, {
        field: {
          label: 'Status',
          options: [
            { value: 'open', label: 'Open' },
            { value: 'posted', label: 'Posted' },
          ],
        },
        state: state.value,
        lookupItems: [],
        'onUpdate:raw': (value: string) => {
          state.value = { ...state.value, raw: value }
        },
      }),
      h('div', `raw:${state.value.raw}`),
    ])
  },
})

const InputHarness = defineComponent({
  setup() {
    const state = ref({
      raw: '42',
      items: [],
      includeDescendants: false,
    })

    return () => h('div', [
      h(NgbFilterFieldControl, {
        field: {
          label: 'Amount',
          dataType: 'Decimal',
        },
        state: state.value,
        lookupItems: [],
        'onUpdate:raw': (value: string) => {
          state.value = { ...state.value, raw: value }
        },
      }),
      h('div', `raw:${state.value.raw}`),
    ])
  },
})

test('wires single lookup branch, open action, clear/select, and include-descendants toggle', async () => {
  const view = await render(LookupHarness)

  await expect.element(view.getByText('lookup-value:Riverfront Tower')).toBeVisible()
  await expect.element(view.getByText('lookup-items:Harbor Tower|Market Square')).toBeVisible()
  await expect.element(view.getByText('Include descendants')).toBeVisible()

  const lookupInput = document.querySelector('[data-testid="stub-lookup"] input')
  if (!(lookupInput instanceof HTMLInputElement)) throw new Error('Lookup input not found.')
  lookupInput.value = 'tower'
  lookupInput.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByText('query-log:tower')).toBeVisible()

  const buttons = document.querySelectorAll('[data-testid="stub-lookup"] button')
  ;(buttons[0] as HTMLButtonElement).click()
  await expect.element(view.getByText('selected:Harbor Tower')).toBeVisible()

  await expect.element(view.getByTestId('stub-switch')).toBeVisible()
  await view.getByTestId('stub-switch').click()
  await expect.element(view.getByText('descendants:true')).toBeVisible()

  ;(buttons[1] as HTMLButtonElement).click()
  await expect.element(view.getByText('selected:none')).toBeVisible()

  ;(buttons[2] as HTMLButtonElement).click()
  await expect.element(view.getByText('opens:1')).toBeVisible()
})

test('wires multi-lookup branch through NgbMultiSelect', async () => {
  const view = await render(MultiLookupHarness)

  await expect.element(view.getByTestId('stub-multi-select')).toBeVisible()
  await expect.element(view.getByText('multi-items:Invoice INV-001|Invoice INV-002')).toBeVisible()

  const input = document.querySelector('[data-testid="stub-multi-select"] input')
  if (!(input instanceof HTMLInputElement)) throw new Error('Multi-select input not found.')
  input.value = 'INV'
  input.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByText('query-log:INV')).toBeVisible()
  await view.getByRole('button', { name: 'Select many' }).click()
  await expect.element(view.getByText('selected:Invoice INV-001|Invoice INV-002', { exact: true })).toBeVisible()
})

test('renders select branch for option filters and input branch for free-form values', async () => {
  const selectView = await render(SelectHarness)

  await expect.element(selectView.getByTestId('stub-select')).toBeVisible()
  await expect.element(selectView.getByText('select:Any|Open|Posted')).toBeVisible()
  await selectView.getByTestId('stub-select').click()
  await expect.element(selectView.getByText('raw:selected')).toBeVisible()

  const inputView = await render(InputHarness)
  await expect.element(inputView.getByTestId('stub-input')).toBeVisible()
  await expect.element(inputView.getByText('input:number:42')).toBeVisible()
  await inputView.getByTestId('stub-input').click()
  await expect.element(inputView.getByText('raw:42')).toBeVisible()
})
