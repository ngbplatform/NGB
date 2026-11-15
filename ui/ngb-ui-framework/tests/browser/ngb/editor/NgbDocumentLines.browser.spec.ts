import { mount } from '@vue/test-utils'
import { h, nextTick } from 'vue'
import { describe, expect, test, vi } from 'vitest'
import { page, userEvent } from 'vitest/browser'

import NgbDocumentLines from '../../../../src/ngb/editor/NgbDocumentLines.vue'
import NgbInput from '../../../../src/ngb/primitives/NgbInput.vue'
import NgbLookup from '../../../../src/ngb/primitives/NgbLookup.vue'

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
  test.each([false, true])('centers real controls and text in tall rows and retains source navigation (readonly=%s)', async (readonly) => {
    await page.viewport(1280, 900)
    const open = vi.fn()
    const wrapper = mount(NgbDocumentLines, {
      attachTo: document.body,
      props: {
        title: 'Lines',
        rows: [{ id: 'one' }],
        columns: [
          { key: 'description', label: 'Description' },
          { key: 'source', label: 'Source' },
          { key: 'quantity', label: 'Quantity' },
          { key: 'amount', label: 'Amount' },
        ],
        readonly,
        readonlyActions: 'disable',
      },
      slots: {
        cell: ({ column }: { column: { key: string } }) => {
          if (column.key === 'description') {
            return h('div', { style: 'height: 96px' }, 'A multiline description makes this row taller than a control.')
          }
          if (column.key === 'source') {
            return h(NgbLookup, {
              modelValue: { id: 'source-one', label: 'Approved order' },
              items: [],
              readonly,
              showOpen: true,
              variant: 'grid',
              onOpen: open,
            })
          }
          if (column.key === 'quantity') {
            return h(NgbInput, { modelValue: 2, type: 'number', variant: 'grid', readonly })
          }
          return h('span', { class: 'inline-flex min-h-8 items-center px-2', 'data-testid': 'amount' }, '25.00')
        },
      },
    })
    try {
      const row = wrapper.get('tbody tr').element.getBoundingClientRect()
      for (const selector of ['input[role="combobox"]', 'input[type="number"]', '[data-testid="amount"]', 'button[title="Delete"]']) {
        const rect = wrapper.get(selector).element.getBoundingClientRect()
        expect(Math.abs((rect.top + rect.bottom) / 2 - (row.top + row.bottom) / 2)).toBeLessThanOrEqual(1)
      }
      await userEvent.click(wrapper.get('button[title="Open"]').element)
      expect(open).toHaveBeenCalledOnce()
      await wrapper.setProps({ errors: { 0: { quantity: 'Quantity exceeds the available amount.' } } })
      expect(wrapper.text()).toContain('Quantity exceeds the available amount.')
      expect(getComputedStyle(wrapper.get('[data-validation-cell="0:quantity"]').element).verticalAlign).toBe('middle')
    } finally {
      wrapper.unmount()
    }
  })

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

  test('focuses the first valid error and falls back to meaningful summary errors', async () => {
    const wrapper = render({
      description: 'Review the lines before posting.',
      summaryErrors: [' ', 'Review the total'],
      errors: {
        0: { name: ' ' },
        1: { name: 'Quantity is required' },
        9: { name: 'This row was removed' },
      },
    })
    const scroll = vi.spyOn(wrapper.element, 'scrollIntoView')
    expect(wrapper.text()).toContain('Review the lines before posting.')
    expect(wrapper.findAll('li').map((item) => item.text())).toEqual(['Review the total'])
    expect(wrapper.vm.focusFirstError()).toBe(true)
    expect(document.activeElement).toBe(wrapper.findAll('input')[1]!.element)
    expect(scroll).not.toHaveBeenCalled()

    await wrapper.setProps({ errors: { 0: {}, 9: { name: 'This row was removed' } } })
    expect(wrapper.vm.focusFirstError()).toBe(true)
    expect(scroll).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' })
    await wrapper.setProps({ errors: null, summaryErrors: [' '] })
    expect(wrapper.vm.focusFirstError()).toBe(false)
    expect(wrapper.find('ul').exists()).toBe(false)
    expect(wrapper.vm.focusCell(0.5, 'name')).toBe(false)
    expect(wrapper.vm.focusCell(0, 'unknown')).toBe(false)

    await wrapper.setProps({ summaryErrors: ['Review the total'] })
    const focusFirstError = wrapper.vm.focusFirstError
    wrapper.unmount()
    expect(() => focusFirstError()).not.toThrow()
    scroll.mockRestore()
  })

  test('navigates bounded pages and normalizes unsupported page sizes', async () => {
    const many = Array.from({ length: 101 }, (_, index) => ({ id: String(index), name: `Row ${index}` }))
    const wrapper = render({ rows: many, pageSize: Number.NaN, rowKey: (row: typeof rows[number]) => row.id })
    const previous = () => wrapper.findAll('button').find((button) => button.text() === 'Previous')!
    const next = () => wrapper.findAll('button').find((button) => button.text() === 'Next')!
    expect(wrapper.findAll('tbody tr')).toHaveLength(100)
    expect(previous().attributes('disabled')).toBeDefined()
    await next().trigger('click')
    expect(wrapper.text()).toContain('Rows 101–101 of 101')
    expect(next().attributes('disabled')).toBeDefined()
    await previous().trigger('click')
    expect(wrapper.text()).toContain('Rows 1–100 of 101')
    await wrapper.setProps({ pageSize: 0 })
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    await wrapper.setProps({ pageSize: 2.9 })
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    wrapper.unmount()
  })

  test('accepts a new row while its columns are not yet available', async () => {
    const wrapper = render({ rows: [], columns: [] })
    await wrapper.setProps({ onAdd: () => void wrapper.setProps({ rows: [rows[0]!] }) })
    await wrapper.findAll('button').find((button) => button.text() === 'Add row')!.trigger('click')
    await nextTick()
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
    expect(wrapper.find('input').exists()).toBe(false)
    await wrapper.setProps({ columns })
    expect(wrapper.vm.focusCell(0, 'name')).toBe(true)
    expect(document.activeElement).toBe(wrapper.get('input').element)
    wrapper.unmount()
  })

  test('accepts only local drags and only the supported reorder keys', async () => {
    const wrapper = render()
    const tableRows = wrapper.findAll('tbody tr')
    const dispatchDrag = async (rowIndex: number, type: string, dataTransfer: DataTransfer | null = null) => {
      const event = new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer })
      tableRows[rowIndex]!.element.dispatchEvent(event)
      await nextTick()
      return event
    }
    expect((await dispatchDrag(1, 'dragover')).defaultPrevented).toBe(false)
    await dispatchDrag(0, 'dragstart')
    expect((await dispatchDrag(1, 'dragover')).defaultPrevented).toBe(true)
    await dispatchDrag(1, 'drop')
    expect(wrapper.emitted('reorder')).toEqual([[0, 1]])

    const grip = wrapper.get('button[aria-label="Reorder row 1"]')
    await grip.trigger('keydown', { key: 'ArrowDown' })
    await grip.trigger('keydown', { altKey: true, key: 'Enter' })
    expect(wrapper.emitted('reorder')).toHaveLength(1)
    await grip.trigger('keydown', { altKey: true, key: 'ArrowDown' })
    expect(wrapper.emitted('reorder')).toEqual([[0, 1], [0, 1]])
    await wrapper.get('button[aria-label="Reorder row 2"]').trigger('keydown', { altKey: true, key: 'ArrowDown' })
    expect(wrapper.emitted('reorder')).toHaveLength(2)

    await dispatchDrag(0, 'dragstart')
    await wrapper.setProps({ readonly: true })
    expect((await dispatchDrag(1, 'dragover')).defaultPrevented).toBe(false)
    await dispatchDrag(1, 'drop')
    await dispatchDrag(0, 'dragstart')
    expect(wrapper.emitted('reorder')).toHaveLength(2)
    wrapper.unmount()
  })

  test('rechecks permissions for row commands captured before the readonly update', async () => {
    const wrapper = render({ readonlyActions: 'disable' })
    const add = wrapper.findAll('button').find((button) => button.text() === 'Add row')!.element
    const remove = wrapper.get('button[title="Delete"]').element
    const grip = wrapper.get('button[aria-label="Reorder row 1"]').element
    await wrapper.setProps({ readonly: true })
    add.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    remove.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    grip.dispatchEvent(new KeyboardEvent('keydown', { altKey: true, key: 'ArrowDown', bubbles: true }))
    await nextTick()
    expect(wrapper.emitted('add')).toBeUndefined()
    expect(wrapper.emitted('remove')).toBeUndefined()
    expect(wrapper.emitted('reorder')).toBeUndefined()
    wrapper.unmount()
  })
})
