import { beforeEach, describe, expect, it, vi } from 'vitest'

const storageState = vi.hoisted(() => ({
  session: new Map<string, string>(),
}))

vi.mock('../../../../src/ngb/utils/storage', () => ({
  listStorageKeys: vi.fn((scope: 'session' | 'local') => Array.from(storageState[scope].keys())),
  readStorageJsonOrNull: vi.fn((scope: 'session' | 'local', key: string) => {
    const raw = storageState[scope].get(key)
    return raw ? JSON.parse(raw) : null
  }),
  readStorageString: vi.fn((scope: 'session' | 'local', key: string) => storageState[scope].get(key) ?? null),
  removeStorageItem: vi.fn((scope: 'session' | 'local', key: string) => {
    storageState[scope].delete(key)
  }),
  writeStorageJson: vi.fn((scope: 'session' | 'local', key: string, value: unknown) => {
    storageState[scope].set(key, JSON.stringify(value))
    return true
  }),
  writeStorageString: vi.fn((scope: 'session' | 'local', key: string, value: string) => {
    storageState[scope].set(key, value)
    return true
  }),
}))

import {
  clearReportPageExecutionSnapshot,
  clearReportPageScrollTop,
  loadReportPageExecutionSnapshot,
  loadReportPageScrollTop,
  saveReportPageExecutionSnapshot,
  saveReportPageScrollTop,
} from '../../../../src/ngb/reporting/pageSession'
import { ReportRowKind, type ReportExecutionResponseDto } from '../../../../src/ngb/reporting/types'

function buildResponse(): ReportExecutionResponseDto {
  return {
    sheet: {
      columns: [
        { code: 'property', title: 'Property', dataType: 'string' },
      ],
      rows: [
        {
          rowKind: ReportRowKind.Detail,
          cells: [
            { display: 'Riverfront Tower', value: 'Riverfront Tower', valueType: 'string' },
          ],
        },
      ],
      meta: {
        title: 'Occupancy Summary',
      },
    },
    offset: 0,
    limit: 100,
    total: 1,
    hasMore: false,
    nextCursor: null,
  }
}

describe('reporting page session helpers', () => {
  beforeEach(() => {
    storageState.session.clear()
  })

  it('stores and restores execution snapshots with normalized cursors', () => {
    saveReportPageExecutionSnapshot('report:ctx', buildResponse(), ['cursor-1', ' ', 'cursor-1', 'cursor-2'])

    expect(loadReportPageExecutionSnapshot('report:ctx')).toEqual({
      response: buildResponse(),
      consumedCursors: ['cursor-1', 'cursor-2'],
    })
  })

  it('does not synchronously persist oversized report sheets', () => {
    const response = buildResponse()
    response.sheet.rows = Array.from({ length: 501 }, (_, index) => ({
      rowKind: ReportRowKind.Detail,
      cells: [{ display: `Row ${index}`, value: index, valueType: 'number' }],
    }))
    storageState.session.set('ngb.report.page.execution:report:large', 'stale')

    saveReportPageExecutionSnapshot('report:large', response, ['cursor-1'])

    expect(loadReportPageExecutionSnapshot('report:large')).toBeNull()
    expect(storageState.session.has('ngb.report.page.execution:report:large')).toBe(false)
  })

  it('accepts a malformed null row collection as an empty bounded snapshot', () => {
    const response = buildResponse()
    response.sheet.rows = null as never

    saveReportPageExecutionSnapshot('report:null-rows', response, [])

    expect(loadReportPageExecutionSnapshot('report:null-rows')?.response.sheet.rows).toBeNull()
  })

  it('bounds wide snapshots by bytes and evicts the oldest execution snapshots', () => {
    const wide = buildResponse()
    wide.sheet.rows[0]!.cells[0]!.display = 'x'.repeat(300_000)
    saveReportPageExecutionSnapshot('report:wide', wide, [])
    expect(loadReportPageExecutionSnapshot('report:wide')).toBeNull()

    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-08T12:00:00Z'))
    for (let index = 0; index < 10; index += 1) {
      saveReportPageExecutionSnapshot(`report:${index}`, buildResponse(), [])
      vi.advanceTimersByTime(1)
    }

    const executionKeys = Array.from(storageState.session.keys())
      .filter((key) => key.startsWith('ngb.report.page.execution:'))
    expect(executionKeys).toHaveLength(8)
    expect(loadReportPageExecutionSnapshot('report:0')).toBeNull()
    expect(loadReportPageExecutionSnapshot('report:9')).not.toBeNull()
    vi.useRealTimers()
  })

  it('ignores malformed snapshots and blank keys', () => {
    storageState.session.set('ngb.report.page.execution:broken', JSON.stringify({
      response: {
        rows: [],
      },
      consumedCursors: ['cursor-1'],
    }))

    expect(loadReportPageExecutionSnapshot('broken')).toBeNull()
    expect(loadReportPageExecutionSnapshot('')).toBeNull()

    saveReportPageExecutionSnapshot('', buildResponse(), ['cursor-1'])
    expect(storageState.session.size).toBe(1)
  })

  it('normalizes nullish keys and malformed persisted cursor collections', () => {
    storageState.session.set('ngb.report.page.execution:missing-cursors', JSON.stringify({
      response: buildResponse(),
    }))
    storageState.session.set('ngb.report.page.execution:null-cursors', JSON.stringify({
      response: buildResponse(),
      consumedCursors: [null, ' cursor-1 ', ''],
    }))

    expect(loadReportPageExecutionSnapshot('missing-cursors')).toEqual({
      response: buildResponse(),
      consumedCursors: [],
    })
    expect(loadReportPageExecutionSnapshot('null-cursors')).toEqual({
      response: buildResponse(),
      consumedCursors: ['cursor-1'],
    })
    expect(loadReportPageExecutionSnapshot(null)).toBeNull()
    saveReportPageExecutionSnapshot(undefined, buildResponse(), [])
    clearReportPageExecutionSnapshot(null)
    expect(storageState.session.size).toBe(2)
  })

  it('stores, normalizes, and clears scroll position', () => {
    saveReportPageScrollTop('report:ctx', 128.8)
    expect(loadReportPageScrollTop('report:ctx')).toBe(128)

    saveReportPageScrollTop('report:ctx', 0)
    expect(loadReportPageScrollTop('report:ctx')).toBe(0)

    saveReportPageScrollTop('report:ctx', Number.POSITIVE_INFINITY)
    expect(loadReportPageScrollTop('report:ctx')).toBe(0)

    saveReportPageScrollTop('report:ctx', 75)
    clearReportPageScrollTop('report:ctx')
    expect(loadReportPageScrollTop('report:ctx')).toBe(0)

    saveReportPageExecutionSnapshot('report:ctx', buildResponse(), ['cursor-1'])
    clearReportPageExecutionSnapshot('report:ctx')
    expect(loadReportPageExecutionSnapshot('report:ctx')).toBeNull()
  })

  it('ignores blank scroll keys and invalid persisted positions', () => {
    saveReportPageScrollTop(null, 42)
    expect(loadReportPageScrollTop(undefined)).toBe(0)
    clearReportPageScrollTop('  ')

    storageState.session.set('ngb.report.page.scroll:invalid', 'not-a-number')
    storageState.session.set('ngb.report.page.scroll:negative', '-12')
    expect(loadReportPageScrollTop('invalid')).toBe(0)
    expect(loadReportPageScrollTop('negative')).toBe(0)
  })
})
