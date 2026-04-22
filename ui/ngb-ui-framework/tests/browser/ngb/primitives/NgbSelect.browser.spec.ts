import { page } from 'vitest/browser'
import { afterEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbSelect from '../../../../src/ngb/primitives/NgbSelect.vue'

function dispatchKey(target: HTMLElement, key: string) {
  const keyCodeMap: Record<string, number> = {
    ArrowDown: 40,
    Enter: 13,
    ' ': 32,
  }
  const keyCode = keyCodeMap[key] ?? 0

  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    code: key,
    keyCode,
    which: keyCode,
    bubbles: true,
    cancelable: true,
  }))
  target.dispatchEvent(new KeyboardEvent('keyup', {
    key,
    code: key,
    keyCode,
    which: keyCode,
    bubbles: true,
    cancelable: true,
  }))
}

async function waitForUi(ms: number = 0) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

const selectOptions = [
  { value: 'open', label: 'Open' },
  { value: 'closed', label: 'Closed' },
  { value: 'archived', label: 'Archived' },
]

const SelectHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
    variant: {
      type: String,
      default: 'default',
    },
  },
  setup(props) {
    const value = ref('open')

    return () => h('div', { 'data-testid': 'overflow-container', class: 'h-20 w-64 overflow-hidden border border-ngb-border p-2' }, [
      h(NgbSelect, {
        modelValue: value.value,
        options: selectOptions,
        variant: props.variant as 'default' | 'grid' | 'compact',
        disabled: props.disabled,
        'onUpdate:modelValue': (next: unknown) => {
          value.value = String(next ?? '')
        },
      }),
      h('div', { 'data-testid': 'select-state' }, `state:${value.value}`),
    ])
  },
})

afterEach(() => {
  document.documentElement.classList.remove('dark')
})

test('opens teleported options and updates the selected value', async () => {
  await page.viewport(1280, 900)

  const view = await render(SelectHarness)

  await view.getByRole('button', { name: /Open/i }).click()
  await expect.element(view.getByText('Closed', { exact: true })).toBeVisible()

  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(listbox?.closest('[data-testid="overflow-container"]')).toBeNull()

  await view.getByText('Closed', { exact: true }).click()
  await expect.element(view.getByTestId('select-state')).toHaveTextContent('state:closed')
  await expect.element(view.getByRole('button', { name: /Closed/i })).toBeVisible()
})

test('opens from the keyboard and updates aria-expanded for assistive navigation', async () => {
  await page.viewport(1280, 900)

  const view = await render(SelectHarness)
  const button = view.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement

  button.focus()
  await waitForUi()
  dispatchKey(button, 'ArrowDown')
  await expect.element(view.getByText('Closed', { exact: true })).toBeVisible()
  expect(button.getAttribute('aria-expanded')).toBe('true')
})

test('repositions the teleported listbox when the anchor moves after resize', async () => {
  await page.viewport(1280, 900)

  const view = await render(SelectHarness)
  const button = view.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement
  let rect = { left: 18, top: 12, width: 180, height: 36 }

  button.getBoundingClientRect = () => ({
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

  await view.getByRole('button', { name: /Open/i }).click()
  await waitForUi()
  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(listbox?.style.left).toBe('18px')
  expect(listbox?.style.top).toBe('56px')
  expect(listbox?.style.width).toBe('180px')

  rect = { left: 44, top: 30, width: 220, height: 36 }
  window.dispatchEvent(new Event('resize'))
  await waitForUi()

  expect(listbox?.style.left).toBe('44px')
  expect(listbox?.style.top).toBe('74px')
  expect(listbox?.style.width).toBe('220px')
})

test('flips the teleported listbox above the field when the viewport runs out of space below', async () => {
  await page.viewport(420, 360)

  const view = await render(SelectHarness)
  const button = view.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement
  const rect = { left: 18, top: 300, width: 180, height: 36 }

  button.getBoundingClientRect = () => ({
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

  await view.getByRole('button', { name: /Open/i }).click()
  await waitForUi(20)

  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(Number.parseInt(listbox?.style.top ?? '0', 10)).toBeLessThan(rect.top)
  expect(listbox?.style.maxHeight).toBe('284px')
})

test('supports compact styling', async () => {
  await page.viewport(1280, 900)

  const compactView = await render(SelectHarness, {
    props: {
      variant: 'compact',
    },
  })

  const compactButton = compactView.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement
  expect(compactButton.className).toContain('h-[26px]')
})

test('keeps the control disabled when requested and supports the grid variant', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(SelectHarness, {
    props: {
      disabled: true,
      variant: 'grid',
    },
  })

  const disabledButton = disabledView.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement
  expect(disabledButton.disabled).toBe(true)
  expect(disabledButton.className).toContain('rounded-none')
})

test('keeps the teleported listbox mounted while the theme class changes live', async () => {
  await page.viewport(1280, 900)

  const view = await render(SelectHarness)
  const button = view.getByRole('button', { name: /Open/i }).element() as HTMLButtonElement

  await view.getByRole('button', { name: /Open/i }).click()
  await expect.element(view.getByText('Closed', { exact: true })).toBeVisible()

  const listbox = document.querySelector('[role="listbox"]') as HTMLElement | null
  expect(listbox).not.toBeNull()
  expect(button.getAttribute('aria-expanded')).toBe('true')

  document.documentElement.classList.add('dark')
  await waitForUi()

  await expect.element(view.getByText('Closed', { exact: true })).toBeVisible()
  expect(document.querySelector('[role="listbox"]')).toBe(listbox)
  expect(button.getAttribute('aria-expanded')).toBe('true')

  document.documentElement.classList.remove('dark')
  await waitForUi()

  await expect.element(view.getByText('Closed', { exact: true })).toBeVisible()
  expect(document.querySelector('[role="listbox"]')).toBe(listbox)
})
