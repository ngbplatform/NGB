import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubIcon } from '../accounting/stubs'

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbReportComposerCollectionSection from '../../../../src/ngb/reporting/NgbReportComposerCollectionSection.vue'

type ComposerItem = {
  id: string
  field: string
  label: string
}

const CollectionSectionHarness = defineComponent({
  props: {
    empty: {
      type: Boolean,
      default: false,
    },
    addDisabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const items = ref<ComposerItem[]>(props.empty
      ? []
      : [
          { id: 'row-1', field: 'property', label: 'Property' },
          { id: 'row-2', field: 'lease', label: 'Lease' },
        ])
    const events = ref<string[]>([])

    return () => h('div', [
      h(NgbReportComposerCollectionSection<ComposerItem>, {
        title: 'Row groups',
        addLabel: 'Add row group',
        items: items.value,
        columns: [
          { title: 'Field', width: '45%' },
          { title: 'Label' },
        ],
        emptyMessage: 'No row groups selected.',
        section: 'rows',
        addDisabled: props.addDisabled,
        rowKey: (item) => item.id,
        onAdd: () => {
          events.value = [...events.value, 'add']
        },
        onRemove: (index: number) => {
          events.value = [...events.value, `remove:${index}`]
        },
        onDragstart: ({ section, index }) => {
          events.value = [...events.value, `dragstart:${section}:${index}`]
        },
        onDragover: () => {
          events.value = [...events.value, 'dragover']
        },
        onDrop: ({ section, index }) => {
          events.value = [...events.value, `drop:${section}:${index}`]
        },
      }, {
        cells: ({ item }) => [
          h('td', { class: 'px-3 py-2', key: `${item.id}:field` }, item.field),
          h('td', { class: 'px-3 py-2', key: `${item.id}:label` }, item.label),
        ],
      }),
      h('div', { 'data-testid': 'collection-events' }, events.value.join('|') || 'none'),
    ])
  },
})

test('renders the empty state and respects the disabled add action', async () => {
  await page.viewport(1280, 900)

  const view = await render(CollectionSectionHarness, {
    props: {
      empty: true,
      addDisabled: true,
    },
  })

  await expect.element(view.getByText('No row groups selected.', { exact: true })).toBeVisible()
  expect((view.getByRole('button', { name: 'Add row group' }).element() as HTMLButtonElement).disabled).toBe(true)
})

test('emits add, remove, dragstart, dragover, and drop actions for populated collections', async () => {
  await page.viewport(1280, 900)

  const view = await render(CollectionSectionHarness)

  await expect.element(view.getByText('property', { exact: true })).toBeVisible()
  await expect.element(view.getByText('lease', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Add row group' }).click()
  ;(document.querySelector('button[title="Delete"]') as HTMLButtonElement).click()

  const rows = Array.from(document.querySelectorAll('tbody tr')) as HTMLTableRowElement[]
  rows[0]?.dispatchEvent(new DragEvent('dragstart', { bubbles: true }))
  rows[1]?.dispatchEvent(new DragEvent('dragover', { bubbles: true }))
  rows[1]?.dispatchEvent(new DragEvent('drop', { bubbles: true }))

  await expect.element(view.getByTestId('collection-events')).toHaveTextContent(
    'add|remove:0|dragstart:rows:0|dragover|drop:rows:1',
  )
})
