import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbTreeNode from '../../../../../src/ngb/components/navigation/NgbTreeNode.vue'

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

const rootNode = {
  id: 'root',
  label: 'Accounting',
  badge: '3',
  children: [
    {
      id: 'journals',
      label: 'Journals',
    },
  ],
}

const TreeNodeHarness = defineComponent({
  setup() {
    const expanded = ref(new Set(['root']))
    const selectedId = ref<string | null>(null)
    const selectedLabel = ref('none')

    return () => h('div', { 'data-testid': 'tree-node-wrap' }, [
      h(NgbTreeNode, {
        node: rootNode,
        level: 0,
        expanded: expanded.value,
        selectedId: selectedId.value,
        onToggle: (id: string) => {
          const next = new Set(expanded.value)
          if (next.has(id)) next.delete(id)
          else next.add(id)
          expanded.value = next
        },
        onSelect: (node: { id: string; label: string }) => {
          selectedId.value = node.id
          selectedLabel.value = node.label
        },
      }),
      h('div', { 'data-testid': 'tree-node-state' }, `expanded:${Array.from(expanded.value).join(',') || 'none'};selected:${selectedId.value ?? 'none'};label:${selectedLabel.value}`),
    ])
  },
})

test('toggles nested rows without selecting them and emits selected nodes when the row is clicked', async () => {
  await page.viewport(1280, 900)

  const view = await render(TreeNodeHarness)

  await expect.element(view.getByText('Accounting', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Journals', { exact: true })).toBeVisible()
  await expect.element(view.getByText('3', { exact: true })).toBeVisible()

  const toggle = view.getByRole('button', { name: 'Toggle' }).element() as HTMLButtonElement
  toggle.click()
  await waitForDomUpdate()

  expect(document.body.textContent?.includes('Journals')).toBe(false)
  await expect.element(view.getByTestId('tree-node-state')).toHaveTextContent('expanded:none;selected:none;label:none')

  await view.getByText('Accounting', { exact: true }).click()
  await expect.element(view.getByTestId('tree-node-state')).toHaveTextContent('expanded:none;selected:root;label:Accounting')

  const rootRow = document.querySelector('[data-testid="tree-node-wrap"] > div > div') as HTMLElement
  expect(rootRow.style.paddingLeft).toBe('8px')
})

test('supports keyboard selection and collapse through treeitem semantics', async () => {
  await page.viewport(1280, 900)

  const view = await render(TreeNodeHarness)
  const rootRow = document.querySelector('[role="treeitem"]') as HTMLElement | null
  expect(rootRow).not.toBeNull()
  expect(rootRow?.getAttribute('aria-level')).toBe('1')
  expect(rootRow?.getAttribute('aria-expanded')).toBe('true')

  rootRow?.focus()
  dispatchKey(rootRow!, 'ArrowLeft')
  await waitForDomUpdate()
  expect(rootRow?.getAttribute('aria-expanded')).toBe('false')
  expect(document.body.textContent?.includes('Journals')).toBe(false)

  dispatchKey(rootRow!, 'Enter')
  await expect.element(view.getByTestId('tree-node-state')).toHaveTextContent('expanded:none;selected:root;label:Accounting')
  expect(rootRow?.getAttribute('aria-selected')).toBe('true')
})
