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
})
