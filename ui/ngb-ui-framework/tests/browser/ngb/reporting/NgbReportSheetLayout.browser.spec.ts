import { expect, test } from 'vitest'
import { page } from 'vitest/browser'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import { ReportRowKind, type ReportSheetDto } from '../../../../src/ngb/reporting/types'

const columns: ReportSheetDto['columns'] = [
  { code: 'state', title: 'Queue State', dataType: 'string', width: 130 },
  { code: 'request', title: 'Request', dataType: 'string', width: 170 },
  { code: 'subject', title: 'Subject', dataType: 'string', width: 220 },
  { code: 'requested', title: 'Requested At', dataType: 'date', width: 120 },
  { code: 'days', title: 'Aging Days', dataType: 'int32', width: 110 },
  { code: 'building', title: 'Building', dataType: 'string', width: 220 },
  { code: 'property', title: 'Property', dataType: 'string', width: 220 },
  { code: 'category', title: 'Category', dataType: 'string', width: 160 },
  { code: 'priority', title: 'Priority', dataType: 'string', width: 110 },
  { code: 'requester', title: 'Requested By', dataType: 'string', width: 180 },
  { code: 'workOrder', title: 'Work Order', dataType: 'string', width: 170 },
  { code: 'assignee', title: 'Assigned To', dataType: 'string', width: 180 },
  { code: 'due', title: 'Due By', dataType: 'date', width: 120 },
]

function queueRows(count: number): ReportSheetDto['rows'] {
  return Array.from({ length: count }, (_, index) => ({
    rowKind: ReportRowKind.Detail,
    groupKey: `queue-${index}`,
    cells: [
      'Overdue', `Maintenance Request MR-${index + 1}`,
      index % 3 === 0 ? 'Broken window latch' : 'Long maintenance description '.repeat(8),
      '2026-04-20', '152', '100 Hudson Ave, Hoboken, NJ 07030',
      `Property-${index}-${'x'.repeat(index % 5 === 0 ? 160 : 5)}`,
      'HVAC', 'High', 'Jack Edwards', `Work Order WO-${index + 1}`,
      'Meridian Landscaping LLC', '2026-04-25',
    ].map(display => ({ display, value: display, valueType: 'string' })),
  }))
}

const nextFrame = () => new Promise<void>(resolve => requestAnimationFrame(() => resolve()))

test('keeps queue column geometry stable across virtual scrolling, appends and viewport resizing', async () => {
  await page.viewport(1100, 800)
  const sheet = ref<ReportSheetDto>({ columns, rows: queueRows(500) })
  const hostWidth = ref(900)
  const Harness = defineComponent({
    setup: () => () => h('div', { style: `display:flex;width:${hostWidth.value}px;height:500px;min-width:0;` }, [
      h(NgbReportSheet, { sheet: sheet.value }),
    ]),
  })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
  await router.push('/')
  await router.isReady()
  const view = await render(Harness, { global: { plugins: [router] } })
  const host = view.getByTestId('report-sheet-scroll').element() as HTMLElement
  const table = view.getByTestId('report-sheet-table').element() as HTMLTableElement
  const headers = Array.from(table.tHead!.rows[0]!.cells)
  const widths = () => headers.map(cell => Math.round(cell.getBoundingClientRect().width))
  const expectedWidths = columns.map(column => column.width!)
  async function scrollToLastRow(label: string) {
    // Newly mounted multiline rows replace estimated heights. Keep scrolling as
    // a user would while those measurements extend the remaining scroll range.
    for (let attempt = 0; attempt < 20; attempt += 1) {
      host.scrollTop = host.scrollHeight
      host.dispatchEvent(new Event('scroll'))
      await nextFrame()
      await nextFrame()
      expect(widths()).toEqual(expectedWidths)
      if (table.textContent?.includes(label)) return
    }
    expect(table.textContent).toContain(label)
  }

  await expect.poll(widths).toEqual(expectedWidths)
  expect(table.querySelectorAll('tbody tr:not([aria-hidden])').length).toBeLessThan(100)
  expect(host.scrollWidth).toBeGreaterThan(host.clientWidth)

  await scrollToLastRow('Maintenance Request MR-500')
  expect(widths()).toEqual(expectedWidths)

  sheet.value = { columns, rows: [...sheet.value.rows, ...queueRows(700).slice(500)] }
  hostWidth.value = 480
  await nextFrame()
  host.scrollLeft = 430
  await scrollToLastRow('Maintenance Request MR-700')

  for (let frame = 0; frame < 8; frame += 1) {
    await nextFrame()
    expect(widths()).toEqual(expectedWidths)
    for (const row of Array.from(table.tBodies[0]!.rows).filter(row => !row.hasAttribute('aria-hidden'))) {
      Array.from(row.cells).forEach((cell, index) => {
        const rect = cell.getBoundingClientRect()
        expect(Math.abs(rect.left - headers[index]!.getBoundingClientRect().left)).toBeLessThan(1)
        const content = cell.querySelector('span, button')!
        const range = document.createRange()
        range.selectNodeContents(content)
        for (const textRect of Array.from(range.getClientRects())) {
          expect(textRect.left).toBeGreaterThanOrEqual(rect.left - 1)
          expect(textRect.right).toBeLessThanOrEqual(rect.right + 1)
        }
      })
    }
  }
  expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(window.innerWidth + 1)
  expect(document.querySelectorAll('[data-testid="report-sheet-table"]')).toHaveLength(1)
  expect(table.querySelectorAll('tbody tr:not([aria-hidden])').length).toBeLessThan(100)
})
