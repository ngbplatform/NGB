import { page } from 'vitest/browser'
import { afterEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubIcon } from '../accounting/stubs'

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbLookup from '../../../../src/ngb/primitives/NgbLookup.vue'

type LookupItem = {
  id: string
  label: string
  meta?: string
}

async function waitForDebounce() {
  await new Promise((resolve) => window.setTimeout(resolve, 280))
}

async function wait(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

function dispatchKey(target: HTMLElement, key: string) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }))
}

const SearchHarness = defineComponent({
  setup() {
    const value = ref<LookupItem | null>({
      id: 'property-1',
      label: 'Riverfront Tower',
      meta: 'Property',
    })
    const items = ref<LookupItem[]>([])
    const queries = ref<string[]>([])
    const openCount = ref(0)

    function onQuery(query: string) {
      queries.value = [...queries.value, query]

      const normalized = query.trim().toLowerCase()
      if (normalized === 'cash') {
        items.value = [
          { id: 'cash-id', label: '1100 Cash', meta: 'Asset' },
          { id: 'petty-id', label: '1110 Petty Cash', meta: 'Asset' },
        ]
        return
      }

      items.value = []
    }

    return () => h('div', [
      h(NgbLookup, {
        modelValue: value.value,
        items: items.value,
        showOpen: true,
        showClear: true,
        placeholder: 'Type account code or name…',
        'onUpdate:modelValue': (next: LookupItem | null) => {
          value.value = next
        },
        onQuery,
        onOpen: () => {
          openCount.value += 1
        },
      }),
      h('div', `value:${value.value?.label ?? 'none'}`),
      h('div', `queries:${queries.value.join('|') || 'none'}`),
      h('div', `opens:${openCount.value}`),
    ])
  },
})

const NoResultsHarness = defineComponent({
  setup() {
    const items = ref<LookupItem[]>([])
    const queries = ref<string[]>([])

    return () => h('div', [
      h(NgbLookup, {
        modelValue: null,
        items: items.value,
        placeholder: 'Type account code or name…',
        onQuery: (query: string) => {
          queries.value = [...queries.value, query]
          items.value = []
        },
      }),
      h('div', `queries:${queries.value.join('|') || 'none'}`),
    ])
  },
})

const ReadonlyHarness = defineComponent({
  setup() {
    const value = ref<LookupItem | null>({
      id: 'coa-1',
      label: '1100 Cash',
    })
    const queries = ref<string[]>([])
    const openCount = ref(0)

    return () => h('div', [
      h(NgbLookup, {
        modelValue: value.value,
        items: [],
        readonly: true,
        showOpen: true,
        showClear: true,
        placeholder: 'Type account code or name…',
        'onUpdate:modelValue': (next: LookupItem | null) => {
          value.value = next
        },
        onQuery: (query: string) => {
          queries.value = [...queries.value, query]
        },
        onOpen: () => {
          openCount.value += 1
        },
      }),
      h('div', `value:${value.value?.label ?? 'none'}`),
      h('div', `queries:${queries.value.join('|') || 'none'}`),
      h('div', `opens:${openCount.value}`),
    ])
  },
})

const RaceHarness = defineComponent({
  setup() {
    const value = ref<LookupItem | null>(null)
    const items = ref<LookupItem[]>([])
    const queries = ref<string[]>([])

    function onQuery(query: string) {
      const normalized = query.trim().toLowerCase()
      queries.value = [...queries.value, normalized]

      if (normalized === 'cash') {
        window.setTimeout(() => {
          items.value = [{ id: 'cash-id', label: '1100 Cash', meta: 'Asset' }]
        }, 80)
        return
      }

      if (normalized === 'revenue') {
        window.setTimeout(() => {
          items.value = [{ id: 'revenue-id', label: '4100 Revenue', meta: 'Income' }]
        }, 10)
        return
      }

      items.value = []
    }

    return () => h('div', [
      h(NgbLookup, {
        modelValue: value.value,
        items: items.value,
        placeholder: 'Type account code or name…',
        'onUpdate:modelValue': (next: LookupItem | null) => {
          value.value = next
        },
        onQuery,
      }),
      h('div', `queries:${queries.value.join('|') || 'none'}`),
      h('div', `value:${value.value?.label ?? 'none'}`),
    ])
  },
})

afterEach(() => {
  document.documentElement.classList.remove('dark')
})

test('debounces query, renders options, selects, opens, and clears values', async () => {
  await page.viewport(1280, 900)

  const view = await render(SearchHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('cash')
  await expect.element(view.getByText('Searching…')).toBeVisible()

  await waitForDebounce()

  await expect.element(view.getByText('queries:cash')).toBeVisible()
  await expect.element(view.getByText('1100 Cash')).toBeVisible()

  await view.getByText('1100 Cash').click()
  await expect.element(view.getByText('value:1100 Cash')).toBeVisible()

  await view.getByRole('button', { name: 'Open' }).click()
  await expect.element(view.getByText('opens:1')).toBeVisible()

  await view.getByRole('button', { name: 'Clear' }).click()
  await expect.element(view.getByText('value:none')).toBeVisible()
  await expect.element(view.getByText('queries:cash|')).toBeVisible()
})

test('repositions the teleported options on resize and keeps combobox semantics open', async () => {
  await page.viewport(1280, 900)

  const view = await render(SearchHarness)
  const input = view.getByRole('combobox').element() as HTMLInputElement
  let rect = { left: 24, top: 18, width: 240, height: 36 }

  input.getBoundingClientRect = () => ({
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

  await view.getByRole('combobox').click()
  await view.getByRole('combobox').fill('cash')
  await waitForDebounce()
  await expect.element(view.getByText('1100 Cash')).toBeVisible()

  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(listbox?.style.left).toBe('24px')
  expect(listbox?.style.top).toBe('62px')
  expect(listbox?.style.width).toBe('240px')
  expect(input.getAttribute('aria-expanded')).toBe('true')

  rect = { left: 60, top: 42, width: 280, height: 36 }
  window.dispatchEvent(new Event('resize'))
  await wait()

  expect(listbox?.style.left).toBe('60px')
  expect(listbox?.style.top).toBe('86px')
  expect(listbox?.style.width).toBe('280px')
})

test('shows a no-results panel after a query resolves without matches', async () => {
  await page.viewport(1280, 900)

  const view = await render(NoResultsHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('zzz')
  await waitForDebounce()

  await expect.element(view.getByText('queries:zzz')).toBeVisible()
  await expect.element(view.getByText('No results')).toBeVisible()
})

test('keeps query input readonly while still allowing open and clear actions', async () => {
  await page.viewport(1280, 900)

  const view = await render(ReadonlyHarness)
  const input = view.getByRole('combobox').element() as HTMLInputElement

  expect(input.readOnly).toBe(true)
  expect(input.disabled).toBe(true)

  await view.getByRole('button', { name: 'Open' }).click()
  await expect.element(view.getByText('opens:1')).toBeVisible()

  await view.getByRole('button', { name: 'Clear' }).click()
  await expect.element(view.getByText('value:none')).toBeVisible()
  await expect.element(view.getByText('queries:none')).toBeVisible()
})

test('ignores stale async lookup results when a newer query is already active', async () => {
  await page.viewport(1280, 900)

  const view = await render(RaceHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('cash')
  await waitForDebounce()
  await input.fill('revenue')
  await wait(120)

  expect(document.body.textContent).not.toContain('1100 Cash')

  await waitForDebounce()
  await wait(20)

  await expect.element(view.getByText('queries:cash|revenue')).toBeVisible()
  await expect.element(view.getByText('4100 Revenue')).toBeVisible()
  expect(document.body.textContent).not.toContain('1100 Cash')
})

test('keeps teleported lookup results open and focused while the theme class changes live', async () => {
  await page.viewport(1280, 900)

  const view = await render(SearchHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('cash')
  await waitForDebounce()

  const inputElement = input.element() as HTMLInputElement
  expect(document.activeElement).toBe(inputElement)
  await expect.element(view.getByText('1100 Cash')).toBeVisible()

  document.documentElement.classList.add('dark')
  await wait()

  await expect.element(view.getByText('1100 Cash')).toBeVisible()
  expect(document.activeElement).toBe(inputElement)
  expect(document.querySelector('[role="listbox"]')).not.toBeNull()

  document.documentElement.classList.remove('dark')
  await wait()

  await expect.element(view.getByText('1100 Cash')).toBeVisible()
  expect(document.activeElement).toBe(inputElement)
})

test('removes teleported lookup overlays when the control unmounts mid-search', async () => {
  await page.viewport(1280, 900)

  const view = await render(SearchHarness)
  const input = view.getByRole('combobox')

  await input.click()
  await input.fill('cash')
  await waitForDebounce()
  await expect.element(view.getByText('1100 Cash')).toBeVisible()

  view.unmount()

  expect(document.querySelector('[role="listbox"]')).toBeNull()
  expect(document.body.textContent).not.toContain('1100 Cash')
})
