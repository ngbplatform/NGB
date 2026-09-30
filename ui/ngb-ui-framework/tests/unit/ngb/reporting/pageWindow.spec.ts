import { describe, expect, it } from 'vitest'
import { ReportPageWindow } from '../../../../src/ngb/reporting/pageWindow'
import { ReportRowKind, type ReportExecutionResponseDto } from '../../../../src/ngb/reporting/types'

function page(first: number, count = 100): ReportExecutionResponseDto {
  return {
    sheet: { columns: [{ code: 'id', title: 'ID', dataType: 'int32' }],
      rows: Array.from({ length: count }, (_, i) => ({ rowKind: ReportRowKind.Detail, cells: [{ value: first + i }] })) },
    offset: 0, limit: count, total: null, hasMore: true, nextCursor: String(first + count),
  }
}

describe('report page window', () => {
  it('accepts an initial append and rejects changed columns without losing retained pages', () => {
    const cache = new ReportPageWindow(100)
    expect(cache.response).toBeNull()
    cache.append('0', page(0))
    const incompatible = page(100)
    incompatible.sheet.columns[0]!.code = 'other'
    expect(() => cache.append('100', incompatible)).toThrow('columns changed')
    expect(cache.response?.sheet.rows[0]!.cells[0]!.value).toBe(0)
    expect(cache.retainedCursorCount).toBe(1)
    cache.append('100', page(100))
    expect(() => cache.prepend(incompatible)).toThrow('columns changed')
    expect(cache.response?.sheet.rows[0]!.cells[0]!.value).toBe(100)
    expect(cache.canLoadPrevious).toBe(true)
  })

  it('keeps only the row window after browsing one million rows', () => {
    const cache = new ReportPageWindow()
    cache.reset(page(0))
    for (let first = 100; first < 1_000_000; first += 100) {
      cache.append(String(first), page(first))
      expect(cache.rowCount).toBeLessThanOrEqual(2_000)
      expect(cache.retainedCursorCount).toBeLessThanOrEqual(128)
      expect(cache.retainedCursorBytes).toBeLessThanOrEqual(512 * 1024)
    }
    expect(cache.loadedRowCount).toBe(1_000_000)
    expect(cache.response?.sheet.rows).toHaveLength(2_000)
    expect(cache.response?.sheet.rows[0]?.cells[0]?.value).toBe(998_000)
    expect(cache.previousCursor).toBe('997900')
  })

  it('continues beyond the retained row budget and reloads evicted previous pages', () => {
    const cache = new ReportPageWindow(300)
    cache.reset(page(0))
    for (let first = 100; first < 10_000; first += 100) {
      cache.append(String(first), page(first))
      expect(cache.rowCount).toBeLessThanOrEqual(300)
    }
    expect(cache.response?.sheet.rows[0]?.cells[0]?.value).toBe(9700)
    expect(cache.previousCursor).toBe('9600')
    expect(cache.prepend(page(9600))).toBe(100)
    expect(cache.response?.sheet.rows[0]?.cells[0]?.value).toBe(9600)
    expect(cache.rowCount).toBe(300)
    expect(cache.response?.total).toBeNull()
  })

  it('bounds cursor bytes and stops backward loading when history expires', () => {
    const cache = new ReportPageWindow(100, 1_000_000, 4, 500)
    cache.reset(page(0))
    for (let n = 1; n < 100; n++) {
      cache.append(`${n}-${'x'.repeat(100)}`, page(n * 100))
      expect(cache.retainedCursorCount).toBeLessThanOrEqual(4)
      expect(cache.retainedCursorBytes).toBeLessThanOrEqual(500)
    }
    expect(cache.canLoadPrevious).toBe(true)
    cache.prepend(page(9800))
    expect(cache.canLoadPrevious).toBe(false)
    expect(cache.historyTruncated).toBe(true)
    expect(() => cache.prepend(page(9700))).toThrow('No earlier page')
    cache.reset(page(0))
    expect(cache.historyTruncated).toBe(false)
    expect(cache.retainedCursorCount).toBe(1)
    expect(cache.retainedCursorBytes).toBe(0)
  })

  it('bounds wide page data by bytes and rejects nonadvancing cursors', () => {
    const cache = new ReportPageWindow(1000, 2000)
    cache.reset(page(0, 10))
    const next = page(10, 10)
    next.sheet.rows[0]!.cells[0]!.display = 'x'.repeat(1200)
    expect(cache.append('10', next)).toBe(10)
    expect(cache.rowCount).toBe(10)
    expect(() => cache.append('20', { ...page(20), nextCursor: '20' })).toThrow('did not advance')
    expect(cache.rowCount).toBe(10)
  })

  it.each([498, 499, 500])('counts all loaded rows beyond the window with %i-row pages', size => {
    const cache = new ReportPageWindow()
    expect(cache.loadedRowCount).toBe(0)
    cache.reset(page(0, size))
    for (let index = 1; index < 12; index++) {
      cache.append(String(index * size), page(index * size, size))
      expect(cache.loadedRowCount).toBe((index + 1) * size)
      expect(cache.rowCount).toBeLessThanOrEqual(2_000)
    }
    expect(cache.rowCount).toBe(size * 4)
  })

  it('does not double count previous pages or forward reloads, including after bookmark eviction', () => {
    const cache = new ReportPageWindow(200, 1_000_000, 3)
    cache.reset(page(0))
    for (let first = 100; first <= 600; first += 100) cache.append(String(first), page(first))
    expect(cache.loadedRowCount).toBe(700)
    expect(cache.retainedCursorCount).toBe(3)
    cache.prepend(page(400))
    expect(cache.loadedRowCount).toBe(700)
    expect(cache.historyTruncated).toBe(true)
    cache.append('600', page(600))
    expect(cache.loadedRowCount).toBe(700)
    cache.append('700', page(700))
    expect(cache.loadedRowCount).toBe(800)
  })

  it('excludes grand totals and advances only by rows actually returned', () => {
    const cache = new ReportPageWindow(2)
    const first = page(0, 2)
    first.sheet.rows.push({ rowKind: ReportRowKind.Total, cells: [] })
    cache.reset(first)
    expect(cache.loadedRowCount).toBe(2)
    const empty = page(2, 0)
    empty.nextCursor = 'after-empty'
    cache.append('2', empty)
    expect(cache.loadedRowCount).toBe(2)
    const last = page(2, 1)
    last.sheet.rows.push({ rowKind: ReportRowKind.Detail, semanticRole: 'grand_total', cells: [] })
    last.hasMore = false
    last.nextCursor = null
    cache.append('after-empty', last)
    expect(cache.loadedRowCount).toBe(3)
    expect(cache.rowCount).toBe(2)
    cache.reset(page(0, 0))
    expect(cache.loadedRowCount).toBe(0)
    cache.reset(page(0, 1))
    expect(cache.loadedRowCount).toBe(1)
  })

  it('retains progress when wide pages evict earlier rows by byte budget', () => {
    const cache = new ReportPageWindow(2000, 2000)
    cache.reset(page(0, 10))
    const wide = page(10, 10)
    wide.sheet.rows[0]!.cells[0]!.display = 'x'.repeat(1200)
    cache.append('10', wide)
    expect(cache.rowCount).toBe(10)
    expect(cache.loadedRowCount).toBe(20)
  })

  it('leaves progress unchanged when appending invalid pages', () => {
    const cache = new ReportPageWindow(100)
    cache.append('0', page(0))
    expect(cache.loadedRowCount).toBe(100)
    const incompatible = page(100)
    incompatible.sheet.columns[0]!.code = 'other'
    expect(() => cache.append('100', incompatible)).toThrow('columns changed')
    expect(() => cache.append('100', { ...page(100), nextCursor: '100' })).toThrow('did not advance')
    expect(cache.loadedRowCount).toBe(100)
    cache.append('100', page(100))
    expect(cache.loadedRowCount).toBe(200)
  })

  it('clones progress independently while preserving the forward frontier', () => {
    const cache = new ReportPageWindow(100)
    cache.reset(page(0))
    cache.append('100', page(100))
    cache.append('200', page(200))
    const copy = cache.clone()
    expect(copy.loadedRowCount).toBe(300)
    copy.prepend(page(100))
    copy.append('200', page(200))
    expect(copy.loadedRowCount).toBe(300)
    copy.append('300', page(300))
    expect(copy.loadedRowCount).toBe(400)
    expect(cache.loadedRowCount).toBe(300)
    copy.reset(page(0, 3))
    expect(copy.loadedRowCount).toBe(3)
    expect(cache.loadedRowCount).toBe(300)
  })
})
