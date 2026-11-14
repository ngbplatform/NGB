import { mount } from '@vue/test-utils'
import { h, nextTick } from 'vue'
import { describe, expect, test } from 'vitest'

import NgbDocumentLines from '../../../../src/ngb/editor/NgbDocumentLines.vue'

const columns = [{ key: 'name', label: 'Name', width: '260px' }]
const rows = [{ id: 'a', name: 'First' }, { id: 'b', name: 'Second' }]

function render(props: Record<string, unknown> = {}) {
  return mount(NgbDocumentLines, {
    attachTo: document.body,
    props: { title: 'Lines', rows, columns, ...props },
    slots: {
      cell: ({ row, readonly }: { row: typeof rows[number]; readonly: boolean }) =>
        h('input', { value: row.name, readonly, 'aria-label': 'Name' }),
    },
  })
}

describe('NgbDocumentLines', () => {
  test('renders data, errors and empty state independently of pagination', async () => {
    const wrapper = render({ errors: { 0: { name: [' ', 'Required'], ordinal: 'Invalid order' } } })
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('No rows yet.')
    expect(wrapper.text()).toContain('Required')
    expect(wrapper.text()).toContain('Invalid order')
    expect(wrapper.get('tbody tr').classes()).toContain('bg-red-50/40')
    expect(wrapper.get('colgroup col:nth-child(3)').attributes('style')).toContain('260px')
    expect(wrapper.vm.focusFirstError()).toBe(true)
    expect(document.activeElement).toBe(wrapper.get('input').element)
    await wrapper.setProps({ rows: [] })
    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.text()).toContain('No rows yet.')
    wrapper.unmount()
  })

  test('emits row commands without mutating input and respects structural permissions', async () => {
    const wrapper = render()
    await wrapper.get('button[title="Delete"]').trigger('click')
    await wrapper.findAll('button').find((button) => button.text() === 'Add row')!.trigger('click')
    expect(wrapper.emitted('remove')).toEqual([[0]])
    expect(wrapper.emitted('add')).toEqual([[]])
    expect(rows.map((row) => row.id)).toEqual(['a', 'b'])
    await wrapper.setProps({ allowAddRemoveRows: false, allowReorder: false })
    expect(wrapper.find('button[title="Delete"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Add row')
    expect(wrapper.get('tbody tr').attributes('draggable')).toBe('false')
    wrapper.unmount()
  })

  test('blocks row mutations in readonly mode while retaining cell content', async () => {
    const wrapper = render({ readonly: true, readonlyActions: 'disable' })
    expect(wrapper.get('input').attributes('readonly')).toBeDefined()
    await wrapper.get('button[title="Delete"]').trigger('click')
    await wrapper.get('button[aria-label="Reorder row 1"]').trigger('keydown', { altKey: true, key: 'ArrowDown' })
    expect(wrapper.emitted('remove')).toBeUndefined()
    expect(wrapper.emitted('reorder')).toBeUndefined()
    expect(wrapper.text()).not.toContain('Add row')
    wrapper.unmount()
  })

  test('keeps drag state local and supports accessible keyboard reordering', async () => {
    const first = render()
    const second = render()
    const transfer = new DataTransfer()
    await first.get('tbody tr').trigger('dragstart', { dataTransfer: transfer })
    await second.findAll('tbody tr')[1]!.trigger('drop', { dataTransfer: transfer })
    expect(second.emitted('reorder')).toBeUndefined()
    await first.findAll('tbody tr')[1]!.trigger('drop', { dataTransfer: transfer })
    expect(first.emitted('reorder')).toEqual([[0, 1]])
    await first.get('button[aria-label="Reorder row 2"]').trigger('keydown', { altKey: true, key: 'ArrowUp' })
    expect(first.emitted('reorder')).toEqual([[0, 1], [1, 0]])
    await first.get('button[aria-label="Reorder row 1"]').trigger('keydown', { altKey: true, key: 'ArrowUp' })
    expect(first.emitted('reorder')).toHaveLength(2)
    await first.get('tbody tr').trigger('dragstart', { dataTransfer: transfer })
    await first.get('tbody tr').trigger('dragend')
    await first.findAll('tbody tr')[1]!.trigger('drop', { dataTransfer: transfer })
    expect(first.emitted('reorder')).toHaveLength(2)
    first.unmount()
    second.unmount()
  })

  test('bounds rendered rows, focuses errors on later pages and clamps after deletion', async () => {
    const many = Array.from({ length: 201 }, (_, index) => ({ id: String(index), name: `Row ${index}` }))
    const wrapper = render({ rows: many, errors: { 200: { name: 'Required on last page' } } })
    expect(wrapper.findAll('tbody tr')).toHaveLength(100)
    expect(wrapper.vm.focusFirstError()).toBe(true)
    await nextTick()
    await nextTick()
    expect(wrapper.text()).toContain('Rows 201–201 of 201')
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(document.activeElement).toBe(wrapper.get('input').element)
    await wrapper.setProps({ rows: many.slice(0, 100) })
    expect(wrapper.findAll('tbody tr')).toHaveLength(100)
    expect(wrapper.text()).not.toContain('Page 3')
    expect(wrapper.vm.focusCell(-1, 'name')).toBe(false)
    wrapper.unmount()
  })

  test('shows an appended row on the last page and uses global row indexes for removal', async () => {
    const many = Array.from({ length: 100 }, (_, index) => ({ id: String(index), name: `Row ${index}` }))
    const wrapper = render({ rows: many })
    await wrapper.setProps({
      onAdd: () => {
        void wrapper.setProps({ rows: [...many, { id: 'new', name: 'New' }] })
      },
    })
    await wrapper.findAll('button').find((button) => button.text() === 'Add row')!.trigger('click')
    await nextTick()
    expect(wrapper.text()).toContain('Rows 101–101 of 101')
    expect(wrapper.get('input').element.value).toBe('New')
    await wrapper.get('button[title="Delete"]').trigger('click')
    expect(wrapper.emitted('remove')).toEqual([[100]])
    wrapper.unmount()
  })
})
