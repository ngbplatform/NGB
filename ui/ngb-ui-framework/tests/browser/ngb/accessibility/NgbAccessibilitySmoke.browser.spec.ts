import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbDialog from '../../../../src/ngb/components/NgbDialog.vue'
import NgbNavigationTree from '../../../../src/ngb/components/navigation/NgbNavigationTree.vue'
import NgbLookup from '../../../../src/ngb/primitives/NgbLookup.vue'
import NgbSelect from '../../../../src/ngb/primitives/NgbSelect.vue'
import NgbTabs from '../../../../src/ngb/primitives/NgbTabs.vue'

const DialogHarness = defineComponent({
  setup() {
    const open = ref(true)

    return () => h(NgbDialog, {
      open: open.value,
      title: 'Rename variant',
      subtitle: 'Update the saved report name.',
      'onUpdate:open': (value: boolean) => {
        open.value = value
      },
    }, {
      default: () => h('input', {
        type: 'text',
        'aria-label': 'Variant name',
      }),
    })
  },
})

const TabsHarness = defineComponent({
  setup() {
    const active = ref('summary')

    return () => h(NgbTabs, {
      modelValue: active.value,
      tabs: [
        { key: 'summary', label: 'Summary' },
        { key: 'history', label: 'History' },
      ],
      'onUpdate:modelValue': (value: string) => {
        active.value = value
      },
    }, {
      default: ({ active: current }: { active: string }) => h('div', `Active tab: ${current}`),
    })
  },
})

const NavigationHarness = defineComponent({
  setup() {
    const selectedId = ref<string | null>('reports')

    return () => h(NgbNavigationTree, {
      nodes: [
        {
          id: 'reports',
          label: 'Reports',
          children: [
            { id: 'occupancy', label: 'Occupancy' },
          ],
        },
        {
          id: 'settings',
          label: 'Settings',
        },
      ],
      modelValue: selectedId.value,
      'onUpdate:modelValue': (value: string | null) => {
        selectedId.value = value
      },
    })
  },
})

const LookupAndSelectHarness = defineComponent({
  setup() {
    const lookupValue = ref<{ id: string; label: string } | null>({
      id: 'cash',
      label: '1100 Cash',
    })
    const selectValue = ref('closed')

    return () => h('div', { style: 'width: 320px;' }, [
      h(NgbLookup, {
        modelValue: lookupValue.value,
        items: [
          { id: 'cash', label: '1100 Cash', meta: 'Asset' },
          { id: 'petty-cash', label: '1110 Petty Cash', meta: 'Asset' },
        ],
        placeholder: 'Search accounts',
        showOpen: true,
        showClear: true,
        'onUpdate:modelValue': (value: { id: string; label: string } | null) => {
          lookupValue.value = value
        },
      }),
      h('div', { style: 'margin-top: 16px;' }, [
        h(NgbSelect, {
          modelValue: selectValue.value,
          options: [
            { value: 'open', label: 'Open' },
            { value: 'closed', label: 'Closed' },
          ],
          'onUpdate:modelValue': (value: unknown) => {
            selectValue.value = String(value ?? '')
          },
        }),
      ]),
    ])
  },
})

test('exposes dialog semantics for modal workflows', async () => {
  await page.viewport(1280, 900)

  const view = await render(DialogHarness)
  const dialog = document.querySelector('[role="dialog"]') as HTMLElement | null

  expect(dialog).not.toBeNull()
  expect(dialog?.getAttribute('aria-modal')).toBe('true')
  await expect.element(view.getByText('Rename variant')).toBeVisible()
  await expect.element(view.getByRole('textbox', { name: 'Variant name' })).toBeVisible()
})

test('exposes tablist, tab, and tabpanel relationships', async () => {
  await page.viewport(1280, 900)

  await render(TabsHarness)

  const tablist = document.querySelector('[role="tablist"]') as HTMLElement | null
  const tabs = Array.from(document.querySelectorAll('[role="tab"]')) as HTMLElement[]
  const panel = document.querySelector('[role="tabpanel"]') as HTMLElement | null

  expect(tablist).not.toBeNull()
  expect(tabs).toHaveLength(2)
  expect(tabs[0]?.getAttribute('aria-selected')).toBe('true')
  expect(panel?.getAttribute('aria-labelledby')).toBe(tabs[0]?.id ?? null)
})

test('exposes tree and treeitem semantics for platform navigation', async () => {
  await page.viewport(1280, 900)

  await render(NavigationHarness)

  const tree = document.querySelector('[role="tree"]') as HTMLElement | null
  const treeItems = Array.from(document.querySelectorAll('[role="treeitem"]')) as HTMLElement[]
  const search = document.querySelector('input[aria-label="Search navigation"]') as HTMLInputElement | null

  expect(tree).not.toBeNull()
  expect(treeItems.length).toBeGreaterThanOrEqual(2)
  expect(treeItems[0]?.getAttribute('aria-expanded')).toBe('true')
  expect(search).not.toBeNull()
})

test('keeps combobox and listbox semantics available for lookup and select inputs', async () => {
  await page.viewport(1280, 900)

  const view = await render(LookupAndSelectHarness)

  await expect.element(view.getByRole('combobox')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Open' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Clear' })).toBeVisible()

  await view.getByRole('button', { name: /Closed/i }).click()
  expect(document.querySelector('[role="listbox"]')).not.toBeNull()
})
