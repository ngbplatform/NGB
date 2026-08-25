import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, nextTick, ref } from 'vue'

vi.mock('../../../../../src/ngb/primitives/NgbStatusIcon.vue', async () => {
  const { defineComponent: defineMockComponent, h: renderMockElement } = await import('vue')
  return {
    default: defineMockComponent({
    props: {
      status: {
        type: String,
        default: '',
      },
      title: {
        type: String,
        default: '',
      },
    },
    setup(props) {
      return () => renderMockElement('span', { 'data-testid': `status-${props.status}` }, `status:${props.status}`)
    },
    }),
  }
})

vi.mock('../../../../../src/ngb/primitives/NgbIcon.vue', async () => {
  const { defineComponent: defineMockComponent, h: renderMockElement } = await import('vue')
  return {
    default: defineMockComponent({
    props: {
      name: {
        type: String,
        default: '',
      },
    },
    setup(props) {
      return () => renderMockElement('span', { 'data-testid': `icon-${props.name}` }, `icon:${props.name}`)
    },
    }),
  }
})

import NgbRegisterGrid from '../../../../../src/ngb/components/register/NgbRegisterGrid.vue'
import type {
  RegisterColumn,
  RegisterSortSpec,
} from '../../../../../src/ngb/components/register/registerTypes'

async function flushUi() {
  await nextTick()
  await Promise.resolve()
}

function dispatchKey(target: HTMLElement, key: string, code: string) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    code,
    bubbles: true,
    cancelable: true,
  }))
}

function dispatchMouse(target: HTMLElement, options: MouseEventInit = {}) {
  target.dispatchEvent(new MouseEvent('click', {
    bubbles: true,
    cancelable: true,
    ...options,
  }))
}

function dispatchDrag(target: HTMLElement, type: 'dragstart' | 'drop') {
  target.dispatchEvent(new DragEvent(type, {
    bubbles: true,
    cancelable: true,
  }))
}

function dispatchPointer(target: HTMLElement, type: 'pointerdown' | 'pointermove' | 'pointerup', options: PointerEventInit) {
  target.dispatchEvent(new PointerEvent(type, {
    bubbles: true,
    cancelable: true,
    ...options,
  }))
}

function gridHeader(): HTMLElement {
  const header = Array.from(document.querySelectorAll('div')).find((node) => node.style.gridTemplateColumns.includes('px'))
  expect(header).toBeTruthy()
  return header as HTMLElement
}

function draggableHeader(label: string): HTMLElement {
  const node = viewByLabel(label)
  const header = node.closest('[draggable="true"]')
  expect(header).toBeTruthy()
  return header as HTMLElement
}

function viewByLabel(label: string): HTMLElement {
  const node = Array.from(document.querySelectorAll('button, span'))
    .find((element) => element.textContent?.trim() === label)
  expect(node).toBeTruthy()
  return node as HTMLElement
}

function headerOrder(): string[] {
  return Array.from(document.querySelectorAll('[draggable="true"]'))
    .map((node) => {
      const label = Array.from(node.querySelectorAll('button, span'))
        .map((element) => element.textContent?.trim() ?? '')
        .find((text) => text.length > 0)
      return label ?? ''
    })
    .filter(Boolean)
}

const baseColumns: RegisterColumn[] = [
  {
    key: 'name',
    title: 'Name',
    width: 180,
    pinned: 'left',
  },
  {
    key: 'amount',
    title: 'Amount',
    width: 120,
    align: 'right',
  },
]

const interactiveRows = [
  {
    key: 'row-1',
    name: 'Beta lease',
    amount: 20,
  },
  {
    key: 'row-2',
    name: 'Alpha lease',
    amount: -5,
    status: 2,
  },
  {
    key: 'row-3',
    name: 'Gamma lease',
    amount: 30,
    isMarkedForDeletion: true,
  },
]

const InteractiveGridHarness = defineComponent({
  setup() {
    const sortBy = ref<RegisterSortSpec[]>([])
    const selectedKeys = ref<string[]>([])
    const activated = ref<string[]>([])

    return () => h('div', [
      h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        heightPx: 260,
        columns: baseColumns,
        rows: interactiveRows,
        sortBy: sortBy.value,
        selectedKeys: selectedKeys.value,
        activateOnRowClick: true,
        'onUpdate:sortBy': (value: RegisterSortSpec[]) => {
          sortBy.value = value
        },
        'onUpdate:selectedKeys': (value: string[]) => {
          selectedKeys.value = value
        },
        onRowActivate: (key: string) => {
          activated.value = [...activated.value, key]
        },
      }),
      h('div', { 'data-testid': 'sort-state' }, sortBy.value.map((entry) => `${entry.key}:${entry.dir}`).join('|') || 'none'),
      h('div', { 'data-testid': 'selected-state' }, selectedKeys.value.join('|') || 'none'),
      h('div', { 'data-testid': 'activated-state' }, activated.value.join('|') || 'none'),
    ])
  },
})

const GroupedGridHarness = defineComponent({
  setup() {
    return () => h(NgbRegisterGrid, {
      title: 'Grouped invoices',
      subtitle: 'By property',
      columns: [
        {
          key: 'number',
          title: 'Number',
          width: 180,
        },
        {
          key: 'property',
          title: 'Property',
          width: 180,
        },
      ],
      rows: [
        {
          key: 'row-1',
          number: 'INV-001',
          property: 'Riverfront',
          debit: 100,
          credit: 0,
          status: 2,
        },
        {
          key: 'row-2',
          number: 'INV-002',
          property: 'Riverfront',
          debit: 0,
          credit: 25,
          isMarkedForDeletion: true,
        },
        {
          key: 'row-3',
          number: 'INV-003',
          property: 'Harbor',
          debit: 10,
          credit: 2,
          isActive: false,
        },
      ],
      groupBy: ['property'],
      defaultExpanded: false,
      showTotals: true,
      showGroupToggleIcons: true,
      heightPx: 320,
    }, {
      statusHeader: ({ toggleAllGroups, allGroupsExpanded }: { toggleAllGroups: () => void; allGroupsExpanded: boolean }) =>
        h('button', {
          type: 'button',
          onClick: () => toggleAllGroups(),
        }, allGroupsExpanded ? 'Collapse all groups' : 'Expand all groups'),
    })
  },
})

const LayoutGridHarness = defineComponent({
  setup() {
    return () => h('div', { style: 'width: 360px' }, [
      h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        heightPx: 220,
        columns: [
          {
            key: 'name',
            title: 'Name',
            width: 220,
            pinned: 'left',
          },
          {
            key: 'amount',
            title: 'Amount',
            width: 220,
            align: 'right',
          },
          {
            key: 'status',
            title: 'Status',
            width: 220,
          },
        ],
        rows: [
          {
            key: 'row-1',
            name: 'Beta lease',
            amount: 20,
            status: 'Draft',
          },
        ],
      }),
    ])
  },
})

const stressColumns: RegisterColumn[] = [
  {
    key: 'name',
    title: 'Lease',
    width: 220,
    pinned: 'left',
  },
  {
    key: 'property',
    title: 'Property',
    width: 220,
  },
  {
    key: 'tenant',
    title: 'Tenant',
    width: 220,
  },
  {
    key: 'manager',
    title: 'Manager',
    width: 180,
  },
  {
    key: 'region',
    title: 'Region',
    width: 180,
  },
  {
    key: 'statusLabel',
    title: 'Status',
    width: 160,
  },
  {
    key: 'amount',
    title: 'Amount',
    width: 160,
    align: 'right',
  },
]

const stressRows = Array.from({ length: 320 }, (_, index) => ({
  key: `stress-row-${index + 1}`,
  name: `Lease ${index + 1}`,
  property: `Property ${((index % 14) + 1).toString().padStart(2, '0')}`,
  tenant: `Tenant ${index + 1}`,
  manager: `Manager ${((index % 8) + 1).toString().padStart(2, '0')}`,
  region: `Region ${((index % 5) + 1).toString().padStart(2, '0')}`,
  statusLabel: index % 3 === 0 ? 'Posted' : 'Open',
  amount: 1000 + index,
}))

const StressGridHarness = defineComponent({
  setup() {
    return () => h('div', { style: 'width: 720px;' }, [
      h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        heightPx: 240,
        rowHeightPx: 32,
        columns: stressColumns,
        rows: stressRows,
      }),
    ])
  },
})

test('updates sort state and mouse-driven selection without over-activating modifier clicks', async () => {
  await page.viewport(1280, 900)

  const view = await render(InteractiveGridHarness)

  const nameHeader = view.getByRole('button', { name: /Name/i })
  const amountHeader = view.getByRole('button', { name: /Amount/i })

  await nameHeader.click()
  expect(view.getByTestId('sort-state').element().textContent).toBe('name:asc')

  await nameHeader.click()
  expect(view.getByTestId('sort-state').element().textContent).toBe('name:desc')

  dispatchMouse(amountHeader.element() as HTMLElement, { shiftKey: true })
  await flushUi()
  expect(view.getByTestId('sort-state').element().textContent).toBe('name:desc|amount:asc')

  dispatchMouse(amountHeader.element() as HTMLElement, { shiftKey: true })
  await flushUi()
  expect(view.getByTestId('sort-state').element().textContent).toBe('name:desc|amount:desc')

  await nameHeader.click()
  expect(view.getByTestId('sort-state').element().textContent).toBe('name:asc')

  await view.getByText('Alpha lease').click()
  expect(view.getByTestId('selected-state').element().textContent).toBe('row-2')
  expect(view.getByTestId('activated-state').element().textContent).toBe('row-2')

  dispatchMouse(view.getByText('Gamma lease').element() as HTMLElement, { ctrlKey: true })
  await flushUi()
  expect(view.getByTestId('selected-state').element().textContent).toBe('row-2|row-3')
  expect(view.getByTestId('activated-state').element().textContent).toBe('row-2')

  dispatchMouse(view.getByText('Gamma lease').element() as HTMLElement, { ctrlKey: true })
  await flushUi()
  expect(view.getByTestId('selected-state').element().textContent).toBe('row-2')

  dispatchMouse(view.getByText('Gamma lease').element() as HTMLElement, { metaKey: true })
  await flushUi()
  dispatchMouse(view.getByText('Beta lease').element() as HTMLElement, { shiftKey: true })
  await flushUi()
  expect((view.getByTestId('selected-state').element().textContent ?? '').split('|').sort()).toEqual(['row-1', 'row-2', 'row-3'])
})

test('supports keyboard navigation, selection, and activation through the viewport', async () => {
  await page.viewport(1280, 900)

  const view = await render(InteractiveGridHarness)
  const viewport = document.querySelector('[tabindex="0"]') as HTMLElement | null

  expect(viewport).not.toBeNull()
  viewport?.focus()

  Object.defineProperty(viewport!, 'clientHeight', { configurable: true, value: 36 })
  Object.defineProperty(viewport!, 'scrollTop', { configurable: true, value: 72, writable: true })
  dispatchKey(viewport!, 'ArrowUp', 'ArrowUp')
  dispatchKey(viewport!, 'ArrowDown', 'ArrowDown')
  dispatchKey(viewport!, ' ', 'Space')
  await flushUi()
  dispatchKey(viewport!, ' ', 'Space')
  await flushUi()
  dispatchKey(viewport!, 'Spacebar', 'Space')
  await flushUi()
  dispatchKey(viewport!, 'Escape', 'Escape')
  dispatchKey(viewport!, 'Enter', 'Enter')
  await flushUi()

  expect(view.getByTestId('selected-state').element().textContent).toBe('row-2')
  expect(view.getByTestId('activated-state').element().textContent).toBe('row-2')
})

test('ignores activation and selection after a reactive row list shrinks past the active index', async () => {
  await page.viewport(1280, 900)

  const ShrinkingHarness = defineComponent({
    setup() {
      const rows = ref(interactiveRows)
      const activated = ref<string[]>([])
      const selected = ref<string[]>([])
      return () => h('div', [
        h('button', { type: 'button', onClick: () => { rows.value = interactiveRows.slice(0, 1) } }, 'Shrink rows'),
        h(NgbRegisterGrid, {
          showPanel: false,
          showTotals: false,
          columns: baseColumns,
          rows: rows.value,
          selectedKeys: selected.value,
          'onUpdate:selectedKeys': (value: string[]) => { selected.value = value },
          onRowActivate: (key: string) => { activated.value = [...activated.value, key] },
        }),
        h('div', { 'data-testid': 'shrinking-activated' }, activated.value.join('|') || 'none'),
        h('div', { 'data-testid': 'shrinking-selected' }, selected.value.join('|') || 'none'),
      ])
    },
  })
  const view = await render(ShrinkingHarness)
  const viewport = document.querySelector('[tabindex="0"]') as HTMLElement

  dispatchKey(viewport, 'ArrowDown', 'ArrowDown')
  dispatchKey(viewport, 'ArrowDown', 'ArrowDown')
  await view.getByRole('button', { name: 'Shrink rows' }).click()
  dispatchKey(viewport, 'Enter', 'Enter')
  dispatchKey(viewport, ' ', 'Space')
  await flushUi()

  expect(view.getByTestId('shrinking-activated').element().textContent).toBe('none')
  expect(view.getByTestId('shrinking-selected').element().textContent).toBe('none')
})

test('ignores keyboard commands when the register contains no data rows', async () => {
  await page.viewport(1280, 900)

  const view = await render(defineComponent({
    setup() {
      return () => h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        columns: baseColumns,
        rows: [],
      })
    },
  }))
  const viewport = document.querySelector('[tabindex="0"]') as HTMLElement

  dispatchKey(viewport, 'Enter', 'Enter')
  dispatchKey(viewport, ' ', 'Space')
  await flushUi()

  expect(viewport).toBeTruthy()
  expect(view.container.textContent).not.toContain('Beta lease')
})

test('toggles grouped rows, renders status icons, and shows total summaries', async () => {
  await page.viewport(1280, 900)

  const view = await render(GroupedGridHarness)

  await expect.element(view.getByText('Grouped invoices')).toBeVisible()
  await expect.element(view.getByText('Riverfront (2)')).toBeVisible()
  await expect.element(view.getByText('Harbor (1)')).toBeVisible()
  expect(document.body.textContent).not.toContain('INV-001')

  await view.getByRole('button', { name: 'Expand all groups' }).click()

  await expect.element(view.getByText('INV-001')).toBeVisible()
  await expect.element(view.getByText('INV-002')).toBeVisible()
  await expect.element(view.getByText('INV-003')).toBeVisible()
  await expect.element(view.getByTestId('status-posted')).toBeVisible()
  await expect.element(view.getByTestId('status-marked')).toBeVisible()
  await expect.element(view.getByTestId('status-saved')).toBeVisible()
  await expect.element(view.getByText('Total')).toBeVisible()
  expect(document.body.textContent).toContain('110.00')
  expect(document.body.textContent).toContain('27.00')

  await view.getByRole('button', { name: /Riverfront \(2\)/i }).click()
  expect(document.body.textContent).not.toContain('INV-001')
  expect(document.body.textContent).toContain('INV-003')
})

test('renders default panel text, compact group options, array cells, formatters, and boolean boundary values', async () => {
  await page.viewport(1440, 1000)

  const booleanValues: unknown[] = [true, 'true', 1, '1', false]
  const view = await render(defineComponent({
    setup() {
      return () => h(NgbRegisterGrid, {
        fillHeight: true,
        columns: [
          { key: 'items', title: 'Items', width: 180, sortable: false },
          { key: 'wrappedItems', title: 'Wrapped items', width: 180, wrap: true },
          { key: 'formatted', title: 'Formatted', width: 150, align: 'center', wrap: true, format: (value) => `custom:${String(value)}` },
          { key: 'plainNumber', title: 'Plain number', width: 120 },
          { key: 'zeroAmount', title: 'Zero amount', width: 120, align: 'right' },
          { key: 'empty', title: 'Empty', width: 100 },
          { key: 'enabled', title: 'Enabled', width: 100, type: 'checkbox' },
        ],
        rows: booleanValues.map((enabled, index) => ({
          key: `boundary-${index}`,
          category: 'A',
          items: index === 0 ? [null, '', 'alpha'] : index === 1 ? [null, ''] : [`item-${index}`],
          wrappedItems: [`wrapped-${index}`],
          formatted: index,
          plainNumber: index + 1,
          zeroAmount: 0,
          empty: index === 0 ? null : '',
          enabled,
          debit: 0,
          credit: 0,
        })),
        groupBy: ['category'],
        defaultExpanded: true,
        showTotals: false,
        showStatusColumn: false,
        showGroupCounts: false,
        showRowStatusIcons: false,
      })
    },
  }))

  await expect.element(view.getByText('Register', { exact: true })).toBeVisible()
  await expect.element(view.getByText('A', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('A (5)')
  await expect.element(view.getByText('alpha', { exact: true })).toBeVisible()
  await expect.element(view.getByText('custom:0', { exact: true })).toBeVisible()
  await expect.element(view.getByText('1', { exact: true })).toBeVisible()
  expect(document.body.textContent).toContain('—')

  const checkboxes = Array.from(document.querySelectorAll('input[type="checkbox"]')) as HTMLInputElement[]
  expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([true, true, true, true, false])

  await view.getByText('alpha', { exact: true }).click()
})

test('passes meaningful and empty status titles to row status icons', async () => {
  await page.viewport(1280, 900)

  const view = await render(defineComponent({
    setup() {
      return () => h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        columns: [{ key: 'name', title: 'Name', width: 180 }],
        rows: [
          { key: 'status-1', name: 'Titled status', __statusTitle: 'Posted by automation' },
          { key: 'status-2', name: 'Blank status', __statusTitle: '   ' },
        ],
      })
    },
  }))

  await expect.element(view.getByText('Titled status')).toBeVisible()
  await expect.element(view.getByText('Blank status')).toBeVisible()
})

test('can hide row status icons while retaining the status column', async () => {
  await page.viewport(1280, 900)

  const view = await render(defineComponent({
    setup() {
      return () => h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        showRowStatusIcons: false,
        columns: [{ key: 'name', title: 'Name', width: 180 }],
        rows: [{ key: 'status-hidden', name: 'Hidden status icon' }],
      })
    },
  }))

  await expect.element(view.getByText('Hidden status icon')).toBeVisible()
  expect(document.querySelector('[data-testid^="status-"]')).toBeNull()
})

test('reorders headers by drag and persists resized widths into the grid template', async () => {
  await page.viewport(1280, 900)

  await render(LayoutGridHarness)

  expect(headerOrder()).toEqual(['Name', 'Amount', 'Status'])

  const amountHeader = draggableHeader('Amount')
  const statusHeader = draggableHeader('Status')
  dispatchDrag(statusHeader, 'dragstart')
  dispatchDrag(amountHeader, 'drop')
  await flushUi()

  expect(headerOrder()).toEqual(['Name', 'Status', 'Amount'])

  const resizeHandle = amountHeader.querySelector('[title="Resize"]') as HTMLElement | null
  expect(resizeHandle).not.toBeNull()
  resizeHandle!.setPointerCapture ??= () => undefined
  resizeHandle!.releasePointerCapture ??= () => undefined

  dispatchPointer(resizeHandle!, 'pointerdown', {
    pointerId: 1,
    clientX: 220,
  })
  dispatchPointer(resizeHandle!, 'pointermove', {
    pointerId: 1,
    clientX: 300,
  })
  dispatchPointer(resizeHandle!, 'pointerup', {
    pointerId: 1,
    clientX: 300,
  })
  await flushUi()

  expect(gridHeader().style.gridTemplateColumns).toContain('300px')
})

test('ignores incomplete, same-column, and stale drag-and-drop operations', async () => {
  await page.viewport(1280, 900)

  const DynamicHarness = defineComponent({
    setup() {
      const columns = ref<RegisterColumn[]>(baseColumns.map((column) => ({ ...column })))
      return () => h('div', [
        h('button', {
          type: 'button',
          onClick: () => {
            columns.value = [...columns.value, { key: 'status', title: 'Status', width: 120 }]
          },
        }, 'Add dynamic column'),
        h(NgbRegisterGrid, {
          showPanel: false,
          showTotals: false,
          columns: columns.value,
          rows: [{ key: 'row-1', name: 'Lease', amount: 10, status: 'Open' }],
        }),
      ])
    },
  })

  const view = await render(DynamicHarness)
  const nameHeader = draggableHeader('Name')
  const amountHeader = draggableHeader('Amount')

  amountHeader.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true }))
  dispatchDrag(amountHeader, 'drop')
  dispatchDrag(nameHeader, 'dragstart')
  dispatchDrag(nameHeader, 'drop')
  await flushUi()
  expect(headerOrder()).toEqual(['Name', 'Amount'])

  await view.getByRole('button', { name: 'Add dynamic column' }).click()
  const statusHeader = draggableHeader('Status')
  dispatchDrag(nameHeader, 'dragstart')
  dispatchDrag(statusHeader, 'drop')
  dispatchDrag(statusHeader, 'dragstart')
  dispatchDrag(amountHeader, 'drop')
  await flushUi()

  expect(headerOrder()).toEqual(['Name', 'Amount', 'Status'])
})

test('keeps pinned left cells anchored while horizontally scrolling wide registers', async () => {
  await page.viewport(1280, 900)

  const view = await render(LayoutGridHarness)
  const viewport = document.querySelector('[tabindex="0"]') as HTMLElement | null
  const nameCell = view.getByText('Beta lease').element().closest('[style*="position: sticky"]') as HTMLElement | null

  expect(viewport).not.toBeNull()
  expect(nameCell).not.toBeNull()
  expect(nameCell!.style.position).toBe('sticky')
  expect(nameCell!.style.left).toBe('40px')

  const amountTextBefore = Math.round((view.getByText('20.00').element() as HTMLElement).getBoundingClientRect().left)

  viewport!.scrollLeft = 180
  viewport!.dispatchEvent(new Event('scroll', { bubbles: true }))
  await flushUi()

  const amountTextAfter = Math.round((view.getByText('20.00').element() as HTMLElement).getBoundingClientRect().left)

  expect(nameCell!.style.position).toBe('sticky')
  expect(nameCell!.style.left).toBe('40px')
  expect(amountTextAfter).toBeLessThan(amountTextBefore - 100)
})

test('renders checkbox columns as disabled boolean cells', async () => {
  await page.viewport(1280, 900)

  const view = await render(defineComponent({
    setup() {
      return () => h(NgbRegisterGrid, {
        showPanel: false,
        showTotals: false,
        heightPx: 180,
        columns: [
          { key: 'name', title: 'Name', width: 180 },
          { key: 'enabled', title: 'Keycloak', width: 120, type: 'checkbox', align: 'center' },
        ],
        rows: [
          { key: 'row-1', name: 'Alex Carter', enabled: true },
          { key: 'row-2', name: 'Casey Morgan', enabled: false },
        ],
      })
    },
  }))

  await expect.element(view.getByText('Alex Carter')).toBeVisible()
  const checkboxes = Array.from(document.querySelectorAll('input[type="checkbox"]')) as HTMLInputElement[]

  expect(checkboxes).toHaveLength(2)
  expect(checkboxes[0].checked).toBe(true)
  expect(checkboxes[0].disabled).toBe(true)
  expect(checkboxes[1].checked).toBe(false)
  expect(checkboxes[1].disabled).toBe(true)
})

test('virtualizes large register datasets without leaking page overflow and can jump to deep rows', async () => {
  await page.viewport(1280, 900)

  const view = await render(StressGridHarness)
  const viewport = document.querySelector('[tabindex="0"]') as HTMLDivElement | null

  expect(viewport).not.toBeNull()
  await expect.element(view.getByText('Lease 1', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('Lease 320')
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)

  viewport!.scrollTop = 32 * 290
  viewport!.scrollLeft = 420
  viewport!.dispatchEvent(new Event('scroll', { bubbles: true }))
  await flushUi()

  await expect.element(view.getByText('Lease 299', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('Lease 1')
  expect(viewport!.scrollLeft).toBeGreaterThan(0)
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)
})
