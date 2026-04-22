import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbNavigationTree from '../../../../../src/ngb/components/navigation/NgbNavigationTree.vue'

async function waitForDomUpdate() {
  await new Promise((resolve) => window.setTimeout(resolve, 0))
}

function dispatchKey(target: HTMLElement, key: string) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }))
}

const treeNodes = [
  {
    id: 'documents',
    label: 'Documents',
    children: [
      { id: 'sales-orders', label: 'Sales orders' },
      { id: 'purchase-orders', label: 'Purchase orders' },
    ],
  },
  {
    id: 'reports',
    label: 'Reports',
    children: [
      { id: 'cash-flow', label: 'Cash flow', badge: 'New' },
      { id: 'occupancy', label: 'Occupancy' },
    ],
  },
  {
    id: 'settings',
    label: 'Settings',
  },
]

const NavigationTreeHarness = defineComponent({
  setup() {
    const selectedId = ref<string | null>(null)
    const selectedLabel = ref('none')

    return () => h('div', [
      h(NgbNavigationTree, {
        nodes: treeNodes,
        modelValue: selectedId.value,
        'onUpdate:modelValue': (value: string | null) => {
          selectedId.value = value
        },
        onSelect: (node: { label: string }) => {
          selectedLabel.value = node.label
        },
      }),
      h('div', { 'data-testid': 'tree-state' }, `id:${selectedId.value ?? 'none'};label:${selectedLabel.value}`),
    ])
  },
})

test('filters recursively, toggles expanded groups, and emits selected nodes', async () => {
  await page.viewport(1280, 900)

  const view = await render(NavigationTreeHarness)

  await expect.element(view.getByText('Sales orders', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Cash flow', { exact: true })).toBeVisible()
  await expect.element(view.getByText('New', { exact: true })).toBeVisible()

  const toggleButtons = Array.from(document.querySelectorAll('button[aria-label="Toggle"]')) as HTMLButtonElement[]
  toggleButtons[0]?.click()
  await waitForDomUpdate()
  expect(document.body.textContent?.includes('Sales orders')).toBe(false)

  const search = document.querySelector('input[placeholder="Search…"]') as HTMLInputElement
  search.value = 'cash'
  search.dispatchEvent(new Event('input', { bubbles: true }))
  await waitForDomUpdate()

  await expect.element(view.getByText('Reports', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Cash flow', { exact: true })).toBeVisible()
  expect(document.body.textContent?.includes('Documents')).toBe(false)

  await view.getByText('Cash flow', { exact: true }).click()
  await expect.element(view.getByTestId('tree-state')).toHaveTextContent('id:cash-flow;label:Cash flow')
})

test('exposes tree semantics and supports keyboard expand and select flows', async () => {
  await page.viewport(1280, 900)

  const view = await render(NavigationTreeHarness)
  const tree = document.querySelector('[role="tree"]')
  expect(tree).not.toBeNull()

  const documentsRow = view.getByText('Documents', { exact: true }).element().closest('[role="treeitem"]') as HTMLElement | null
  expect(documentsRow).not.toBeNull()
  expect(documentsRow?.getAttribute('aria-level')).toBe('1')
  expect(documentsRow?.getAttribute('aria-expanded')).toBe('true')

  documentsRow?.focus()
  dispatchKey(documentsRow!, 'ArrowLeft')
  await waitForDomUpdate()
  expect(documentsRow?.getAttribute('aria-expanded')).toBe('false')
  expect(document.body.textContent?.includes('Sales orders')).toBe(false)

  dispatchKey(documentsRow!, 'ArrowRight')
  await waitForDomUpdate()
  expect(documentsRow?.getAttribute('aria-expanded')).toBe('true')
  await expect.element(view.getByText('Sales orders', { exact: true })).toBeVisible()

  const settingsRow = view.getByText('Settings', { exact: true }).element().closest('[role="treeitem"]') as HTMLElement | null
  expect(settingsRow).not.toBeNull()
  settingsRow?.focus()
  dispatchKey(settingsRow!, 'Enter')

  await expect.element(view.getByTestId('tree-state')).toHaveTextContent('id:settings;label:Settings')
  expect(settingsRow?.getAttribute('aria-selected')).toBe('true')
})
