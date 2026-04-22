import { beforeEach, describe, expect, it, vi } from 'vitest'

const storageState = vi.hoisted(() => ({
  session: new Map<string, string>(),
}))

vi.mock('../../../../src/ngb/utils/storage', () => ({
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

  it('stores, normalizes, and clears scroll position', () => {
    saveReportPageScrollTop('report:ctx', 128.8)
    expect(loadReportPageScrollTop('report:ctx')).toBe(128)

    saveReportPageScrollTop('report:ctx', 0)
    expect(loadReportPageScrollTop('report:ctx')).toBe(0)

    saveReportPageScrollTop('report:ctx', 75)
    clearReportPageScrollTop('report:ctx')
    expect(loadReportPageScrollTop('report:ctx')).toBe(0)

    saveReportPageExecutionSnapshot('report:ctx', buildResponse(), ['cursor-1'])
    clearReportPageExecutionSnapshot('report:ctx')
    expect(loadReportPageExecutionSnapshot('report:ctx')).toBeNull()
  })
})
