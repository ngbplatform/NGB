import { page } from 'vitest/browser'
import { beforeEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbColumnChooser from '../../../../../src/ngb/components/register/NgbColumnChooser.vue'
import NgbRegisterGrid from '../../../../../src/ngb/components/register/NgbRegisterGrid.vue'
import type { RegisterColumn } from '../../../../../src/ngb/components/register/registerTypes'

const chooserColumns = [
  { id: 'name', label: 'Name' },
  { id: 'amount', label: 'Amount' },
  { id: 'status', label: 'Status' },
]

const gridColumns: RegisterColumn[] = [
  {
    key: 'name',
    title: 'Name',
    width: 180,
  },
  {
    key: 'amount',
    title: 'Amount',
    width: 120,
    align: 'right',
  },
  {
    key: 'status',
    title: 'Status',
    width: 120,
  },
]

const gridRows = [
  {
    key: 'row-1',
    name: 'Lease A',
    amount: 1250,
    status: 'Open',
  },
]

const chooserStorageKey = 'ngb:test:column-chooser'
const integrationStorageKey = 'ngb:test:column-chooser:grid'

const ColumnChooserHarness = defineComponent({
  props: {
    storageKey: {
      type: String,
      default: undefined,
    },
  },
  setup(props) {
    const visible = ref<string[]>(chooserColumns.map((column) => column.id))

    return () => h('div', [
      h('div', { 'data-testid': 'outside-target' }, 'Outside target'),
      h(NgbColumnChooser, {
        columns: chooserColumns,
        modelValue: visible.value,
        storageKey: props.storageKey,
        'onUpdate:modelValue': (value: string[]) => {
          visible.value = value
        },
      }),
      h('div', { 'data-testid': 'visible-state' }, visible.value.join('|') || 'none'),
    ])
  },
})

const ColumnChooserGridHarness = defineComponent({
  props: {
    storageKey: {
      type: String,
      default: integrationStorageKey,
    },
  },
  setup(props) {
    const visibleColumnKeys = ref<string[] | undefined>(undefined)

    return () => h('div', [
      h(NgbColumnChooser, {
        columns: chooserColumns,
        modelValue: visibleColumnKeys.value ?? chooserColumns.map((column) => column.id),
        storageKey: props.storageKey,
        'onUpdate:modelValue': (value: string[]) => {
          visibleColumnKeys.value = value
        },
      }),
      h('div', { 'data-testid': 'integration-visible-state' }, (visibleColumnKeys.value ?? chooserColumns.map((column) => column.id)).join('|')),
      h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        showStatusColumn: false,
        heightPx: 220,
        columns: gridColumns,
        rows: gridRows,
        storageKey: props.storageKey,
        visibleColumnKeys: visibleColumnKeys.value,
      }),
    ])
  },
})

function readStorage(key: string): unknown {
  return JSON.parse(window.localStorage.getItem(key) ?? 'null')
}

function checked(locator: { element(): Element }) {
  return (locator.element() as HTMLInputElement).checked
}

beforeEach(() => {
  window.localStorage.removeItem(chooserStorageKey)
  window.localStorage.removeItem(integrationStorageKey)
})

test('hydrates visible columns from merged storage, toggles values in column order, and preserves order/widths on save', async () => {
  await page.viewport(1280, 900)
  window.localStorage.setItem(chooserStorageKey, JSON.stringify({
    order: ['amount', 'name'],
    widths: {
      amount: 220,
    },
    visible: ['amount'],
  }))

  const view = await render(ColumnChooserHarness, {
    props: {
      storageKey: chooserStorageKey,
    },
  })

  await expect.element(view.getByText('amount', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: /Columns/i }).click()
  await expect.element(view.getByText('Columns', { exact: true })).toBeVisible()

  expect(checked(view.getByRole('checkbox', { name: 'Name' }))).toBe(false)
  expect(checked(view.getByRole('checkbox', { name: 'Amount' }))).toBe(true)
  expect(checked(view.getByRole('checkbox', { name: 'Status' }))).toBe(false)

  await view.getByRole('checkbox', { name: 'Name' }).click()
  await expect.element(view.getByText('name|amount', { exact: true })).toBeVisible()
  expect(readStorage(chooserStorageKey)).toEqual({
    order: ['amount', 'name'],
    widths: {
      amount: 220,
    },
    visible: ['name', 'amount'],
  })

  await view.getByRole('button', { name: 'Reset' }).click()
  await expect.element(view.getByText('name|amount|status', { exact: true })).toBeVisible()
  expect(readStorage(chooserStorageKey)).toEqual({
    order: ['amount', 'name'],
    widths: {
      amount: 220,
    },
    visible: ['name', 'amount', 'status'],
  })
})

test('closes from the close button and outside clicks', async () => {
  await page.viewport(1280, 900)

  const view = await render(ColumnChooserHarness)

  await view.getByRole('button', { name: /Columns/i }).click()
  await expect.element(view.getByText('Columns', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Close' }).click()
  expect(document.body.textContent).not.toContain('Reset')

  await view.getByRole('button', { name: /Columns/i }).click()
  await expect.element(view.getByText('Columns', { exact: true })).toBeVisible()

  await view.getByTestId('outside-target').click()
  expect(document.body.textContent).not.toContain('Reset')
})

test('shares one storage contract with register grid for visible columns, widths, and order', async () => {
  await page.viewport(1280, 900)
  window.localStorage.setItem(integrationStorageKey, JSON.stringify({
    order: ['amount', 'name', 'status'],
    widths: {
      amount: 240,
    },
    visible: ['amount'],
  }))

  const view = await render(ColumnChooserGridHarness)

  await expect.element(view.getByText('amount', { exact: true })).toBeVisible()
  expect(document.body.textContent).toContain('Amount')
  expect(document.body.textContent).not.toContain('Lease A')

  await view.getByRole('button', { name: /Columns/i }).click()
  await view.getByRole('checkbox', { name: 'Name' }).click()

  await expect.element(view.getByText('name|amount', { exact: true })).toBeVisible()
  expect(document.body.textContent).toContain('Lease A')
  expect(readStorage(integrationStorageKey)).toEqual({
    order: ['amount', 'name', 'status'],
    widths: {
      amount: 240,
    },
    visible: ['name', 'amount'],
  })
})

test('falls back to the live column model when persisted chooser storage is malformed', async () => {
  await page.viewport(1280, 900)
  window.localStorage.setItem(chooserStorageKey, '{invalid-json')

  const view = await render(ColumnChooserHarness, {
    props: {
      storageKey: chooserStorageKey,
    },
  })

  await expect.element(view.getByText('name|amount|status', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: /Columns/i }).click()
  expect(checked(view.getByRole('checkbox', { name: 'Name' }))).toBe(true)
  expect(checked(view.getByRole('checkbox', { name: 'Amount' }))).toBe(true)
  expect(checked(view.getByRole('checkbox', { name: 'Status' }))).toBe(true)

  await view.getByRole('checkbox', { name: 'Status' }).click()

  await expect.element(view.getByText('name|amount', { exact: true })).toBeVisible()
  expect(readStorage(chooserStorageKey)).toEqual({
    visible: ['name', 'amount'],
  })
})
