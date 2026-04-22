import { page } from 'vitest/browser'
import { afterEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbMultiSelect from '../../../../src/ngb/primitives/NgbMultiSelect.vue'

type Item = {
  id: string
  label: string
  meta?: string
}

const sourceItems: Item[] = [
  { id: 'alpha', label: 'Alpha', meta: 'Primary' },
  { id: 'bravo', label: 'Bravo', meta: 'Secondary' },
  { id: 'charlie', label: 'Charlie', meta: 'Archive' },
]

async function waitForComboboxDebounce() {
  await new Promise((resolve) => window.setTimeout(resolve, 240))
}

async function wait(ms = 20) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

const MultiSelectHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const items = ref<Item[]>([])
    const selected = ref<Item[]>([{ id: 'alpha', label: 'Alpha', meta: 'Primary' }])
    const queries = ref<string[]>([])

    function onQuery(query: string) {
      queries.value = [...queries.value, query]
      const normalized = query.trim().toLowerCase()

      if (!normalized) {
        items.value = [...sourceItems]
        return
      }

      items.value = sourceItems.filter((item) =>
        `${item.label} ${item.meta ?? ''}`.toLowerCase().includes(normalized),
      )
    }

    return () => h('div', [
      h(NgbMultiSelect, {
        modelValue: selected.value,
        items: items.value,
        placeholder: 'Search accounts',
        disabled: props.disabled,
        'onUpdate:modelValue': (next: Item[]) => {
          selected.value = next
        },
        onQuery,
      }),
      h('div', { 'data-testid': 'multi-state' }, `state:${selected.value.map((item) => item.label).join('|') || 'none'}`),
      h('div', { 'data-testid': 'query-state' }, `queries:${queries.value.join('|') || 'none'}`),
    ])
  },
})

const MultiSelectRaceHarness = defineComponent({
  setup() {
    const items = ref<Item[]>([])
    const selected = ref<Item[]>([])
    const queries = ref<string[]>([])

    function onQuery(query: string) {
      const normalized = query.trim().toLowerCase()
      queries.value = [...queries.value, normalized]

      if (!normalized) {
        items.value = []
        return
      }

      if (normalized === 'cash') {
        window.setTimeout(() => {
          items.value = [{ id: 'cash', label: '1100 Cash', meta: 'Asset' }]
        }, 80)
        return
      }

      if (normalized === 'revenue') {
        window.setTimeout(() => {
          items.value = [{ id: 'revenue', label: '4100 Revenue', meta: 'Income' }]
        }, 10)
        return
      }

      items.value = []
    }

    return () => h('div', [
      h(NgbMultiSelect, {
        modelValue: selected.value,
        items: items.value,
        placeholder: 'Search accounts',
        'onUpdate:modelValue': (next: Item[]) => {
          selected.value = next
        },
        onQuery,
      }),
      h('div', { 'data-testid': 'query-state' }, `queries:${queries.value.join('|') || 'none'}`),
      h('div', { 'data-testid': 'multi-state' }, `state:${selected.value.map((item) => item.label).join('|') || 'none'}`),
    ])
  },
})

afterEach(() => {
  document.documentElement.classList.remove('dark')
})

test('queries, selects, removes, and backspaces chips through the multiselect', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('br')
  await expect.element(view.getByText('Searching…')).toBeVisible()

  await waitForComboboxDebounce()
  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()

  await view.getByText('Bravo', { exact: true }).click()
  await expect.element(view.getByTestId('multi-state')).toHaveTextContent('state:Alpha|Bravo')
  await expect.element(view.getByTestId('query-state')).toHaveTextContent('queries:br|')

  const removeButtons = document.querySelectorAll('button[aria-label="Remove"]')
  expect(removeButtons.length).toBe(2)
  ;(removeButtons[1] as HTMLButtonElement).click()
  await expect.element(view.getByTestId('multi-state')).toHaveTextContent('state:Alpha')

  const inputElement = input.element() as HTMLInputElement
  inputElement.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }))
  await expect.element(view.getByTestId('multi-state')).toHaveTextContent('state:none')
})

test('repositions the teleported multiselect options on resize and keeps combobox semantics open', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectHarness)
  const input = view.getByRole('combobox')
  const inputElement = input.element() as HTMLInputElement
  const anchor = inputElement.closest('.relative') as HTMLElement | null
  let rect = { left: 24, top: 18, width: 240, height: 36 }

  expect(anchor).not.toBeNull()
  anchor!.getBoundingClientRect = () => ({
    x: rect.left,
    y: rect.top,
    left: rect.left,
    top: rect.top,
    width: rect.width,
    height: rect.height,
    right: rect.left + rect.width,
    bottom: rect.top + rect.height,
    toJSON: () => ({}),
  } as DOMRect)

  await input.click()
  await input.fill('br')
  await waitForComboboxDebounce()
  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()

  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(listbox?.style.left).toBe('24px')
  expect(listbox?.style.top).toBe('62px')
  expect(listbox?.style.width).toBe('240px')
  expect(inputElement.getAttribute('aria-expanded')).toBe('true')

  rect = { left: 60, top: 42, width: 280, height: 36 }
  window.dispatchEvent(new Event('resize'))
  await wait()

  expect(listbox?.style.left).toBe('60px')
  expect(listbox?.style.top).toBe('86px')
  expect(listbox?.style.width).toBe('280px')
})

test('shows a no-results panel and keeps the control disabled when requested', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('zzz')
  await waitForComboboxDebounce()
  await expect.element(view.getByText('No results')).toBeVisible()
})

test('keeps the combobox input disabled when requested', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(MultiSelectHarness, {
    props: {
      disabled: true,
    },
  })
  expect((disabledView.getByRole('combobox').element() as HTMLInputElement).disabled).toBe(true)
})

test('ignores stale async multiselect results when a newer query is already active', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectRaceHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('cash')
  await waitForComboboxDebounce()
  await input.fill('revenue')
  await wait(120)

  expect(document.body.textContent).not.toContain('1100 Cash')

  await waitForComboboxDebounce()
  await wait(20)

  await expect.element(view.getByTestId('query-state')).toHaveTextContent('queries:cash|revenue')
  await expect.element(view.getByText('4100 Revenue')).toBeVisible()
  expect(document.body.textContent).not.toContain('1100 Cash')
})

test('keeps teleported multiselect results open and focused while the theme class changes live', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('br')
  await waitForComboboxDebounce()

  const inputElement = input.element() as HTMLInputElement
  expect(document.activeElement).toBe(inputElement)
  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()

  document.documentElement.classList.add('dark')
  await wait()

  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()
  expect(document.activeElement).toBe(inputElement)
  expect(document.querySelector('[role="listbox"]')).not.toBeNull()

  document.documentElement.classList.remove('dark')
  await wait()

  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()
  expect(document.activeElement).toBe(inputElement)
})

test('removes teleported multiselect overlays when the control unmounts mid-search', async () => {
  await page.viewport(1280, 900)

  const view = await render(MultiSelectHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('br')
  await waitForComboboxDebounce()
  await expect.element(view.getByText('Bravo', { exact: true })).toBeVisible()

  view.unmount()

  expect(document.querySelector('[role="listbox"]')).toBeNull()
  expect(document.body.textContent).not.toContain('Bravo')
})
