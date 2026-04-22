import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbTabs from '../../../../src/ngb/primitives/NgbTabs.vue'

function dispatchKey(target: HTMLElement, key: string) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }))
}

const TabsHarness = defineComponent({
  setup() {
    const active = ref('summary')

    return () => h('div', [
      h(NgbTabs, {
        modelValue: active.value,
        tabs: [
          { key: 'summary', label: 'Summary' },
          { key: 'details', label: 'Details' },
          { key: 'history', label: 'History' },
        ],
        fill: true,
        fullWidthBar: true,
        'onUpdate:modelValue': (value: string) => {
          active.value = value
        },
      }, {
        default: ({ active: slotActive }: { active: string }) =>
          h('div', { 'data-testid': 'tabs-slot' }, `slot:${slotActive}`),
      }),
      h('div', { 'data-testid': 'tabs-state' }, `state:${active.value}`),
    ])
  },
})

test('switches the active tab and updates the slot context', async () => {
  await page.viewport(1280, 900)

  const view = await render(TabsHarness)

  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:summary')
  await expect.element(view.getByTestId('tabs-slot')).toHaveTextContent('slot:summary')

  await view.getByRole('tab', { name: 'Details' }).click()
  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:details')
  await expect.element(view.getByTestId('tabs-slot')).toHaveTextContent('slot:details')

  await view.getByRole('tab', { name: 'History' }).click()
  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:history')
  await expect.element(view.getByTestId('tabs-slot')).toHaveTextContent('slot:history')
})

test('exposes tab semantics and supports arrow, home, and end keyboard navigation', async () => {
  await page.viewport(1280, 900)

  const view = await render(TabsHarness)
  const tablist = view.getByRole('tablist')
  const summary = view.getByRole('tab', { name: 'Summary' }).element() as HTMLButtonElement
  const details = view.getByRole('tab', { name: 'Details' }).element() as HTMLButtonElement
  const history = view.getByRole('tab', { name: 'History' }).element() as HTMLButtonElement

  expect(tablist.element().getAttribute('aria-orientation')).toBe('horizontal')
  expect(summary.getAttribute('aria-selected')).toBe('true')
  expect(summary.tabIndex).toBe(0)
  expect(details.getAttribute('aria-selected')).toBe('false')
  expect(details.tabIndex).toBe(-1)

  summary.focus()
  dispatchKey(summary, 'ArrowRight')
  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:details')
  expect(document.activeElement).toBe(details)

  dispatchKey(details, 'End')
  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:history')
  expect(document.activeElement).toBe(history)

  dispatchKey(history, 'Home')
  await expect.element(view.getByTestId('tabs-state')).toHaveTextContent('state:summary')
  expect(document.activeElement).toBe(summary)
})
