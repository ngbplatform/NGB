import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import {
  StubDrawer,
  StubFilterFieldControl,
  StubFormLayout,
  StubFormRow,
  StubIcon,
} from './stubs'

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', () => ({
  default: StubDrawer,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormLayout.vue', () => ({
  default: StubFormLayout,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormRow.vue', () => ({
  default: StubFormRow,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/metadata/NgbFilterFieldControl.vue', () => ({
  default: StubFilterFieldControl,
}))

import NgbDocumentListFiltersDrawer from '../../../../src/ngb/metadata/NgbDocumentListFiltersDrawer.vue'

const DrawerHarness = defineComponent({
  props: {
    empty: {
      type: Boolean,
      default: false,
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    canUndo: {
      type: Boolean,
      default: true,
    },
  },
  setup(props) {
    const open = ref(true)
    const events = ref<string[]>([])

    return () => h('div', [
      h(NgbDocumentListFiltersDrawer, {
        open: open.value,
        filters: props.empty ? [] : [
          {
            key: 'status',
            label: 'Status',
            description: 'Current workflow state',
          },
        ],
        values: {
          status: {
            raw: 'open',
            items: [],
          },
        },
        lookupItemsByKey: {
          status: [{ id: 'posted', label: 'Posted' }],
        },
        canUndo: props.canUndo,
        disabled: props.disabled,
        'onUpdate:open': (value: boolean) => {
          open.value = value
          events.value.push(`open:${String(value)}`)
        },
        onLookupQuery: (payload: { key: string; query: string }) => events.value.push(`lookup:${payload.key}:${payload.query}`),
        'onUpdate:items': (payload: { key: string; items: Array<{ id: string; label: string }> }) =>
          events.value.push(`items:${payload.key}:${payload.items.map((item) => item.label).join('|')}`),
        'onUpdate:value': (payload: { key: string; value: string }) => events.value.push(`value:${payload.key}:${payload.value}`),
        onUndo: () => events.value.push('undo'),
      }),
      h('div', { 'data-testid': 'drawer-events' }, events.value.join('|')),
    ])
  },
})

test('renders filters and forwards lookup, item, value, undo, and close events', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerHarness)

  await expect.element(view.getByText('drawer-title:Filter')).toBeVisible()
  await expect.element(view.getByText('drawer-subtitle:Define criteria to refine results')).toBeVisible()
  await expect.element(view.getByText('row-label:Status')).toBeVisible()
  await expect.element(view.getByText('row-hint:Current workflow state')).toBeVisible()
  await expect.element(view.getByTestId('stub-filter-field:status')).toHaveTextContent('state-raw:open')
  await expect.element(view.getByTestId('icon-undo')).toBeVisible()

  await view.getByRole('button', { name: 'Field lookup query' }).click()
  await view.getByRole('button', { name: 'Field set items' }).click()
  await view.getByRole('button', { name: 'Field set raw' }).click()
  await view.getByRole('button', { name: 'Undo' }).click()
  await view.getByRole('button', { name: 'Drawer close' }).click()

  await expect.element(view.getByTestId('drawer-events')).toHaveTextContent(
    'lookup:status:river|items:status:Riverfront Tower|value:status:posted|undo|open:false',
  )
})

test('renders the empty state when there are no available filters', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerHarness, {
    props: {
      empty: true,
      canUndo: false,
    },
  })

  await expect.element(view.getByText('No filters available.')).toBeVisible()
  expect((view.getByRole('button', { name: 'Undo' }).element() as HTMLButtonElement).disabled).toBe(true)
})
