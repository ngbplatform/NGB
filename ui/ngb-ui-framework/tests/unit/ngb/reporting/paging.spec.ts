import { describe, expect, it } from 'vitest'

import {
  buildAppendRequest,
  canAppendReportResponse,
  countLoadedReportRows,
  hasReachedReportRowLimit,
  mergePagedReportResponses,
} from '../../../../src/ngb/reporting/paging'
import {
  ReportRowKind,
  type ReportExecutionRequestDto,
  type ReportExecutionResponseDto,
  type ReportSheetDto,
} from '../../../../src/ngb/reporting/types'

function createSheet(rows: ReportSheetDto['rows'], overrides: Partial<ReportSheetDto> = {}): ReportSheetDto {
  return {
    columns: [
      { code: 'name', title: 'Name', dataType: 'string' },
    ],
    rows,
    meta: null,
    headerRows: null,
    ...overrides,
  }
}

function createResponse(
  rows: ReportSheetDto['rows'],
  overrides: Partial<ReportExecutionResponseDto> = {},
): ReportExecutionResponseDto {
  return {
    sheet: createSheet(rows),
    offset: 0,
    limit: 50,
    total: rows.length,
    hasMore: false,
    nextCursor: null,
    diagnostics: { source: 'current' },
    ...overrides,
  }
}

describe('report paging helpers', () => {
  it('counts loaded rows without including grand total rows', () => {
    const count = countLoadedReportRows(createSheet([
      {
        rowKind: ReportRowKind.Group,
        cells: [],
      },
      {
        rowKind: ReportRowKind.Detail,
        cells: [],
      },
      {
        rowKind: ReportRowKind.Total,
        cells: [],
      },
      {
        rowKind: ReportRowKind.Detail,
        cells: [],
        semanticRole: 'grand_total',
      },
    ]))

    expect(count).toBe(2)
    expect(countLoadedReportRows(undefined)).toBe(0)
    expect(countLoadedReportRows(createSheet([null as never]))).toBe(1)
    expect(hasReachedReportRowLimit(createSheet([]))).toBe(false)
    expect(hasReachedReportRowLimit(createSheet(Array.from({ length: 2_000 }, () => ({
      rowKind: ReportRowKind.Detail,
      cells: [],
    }))))).toBe(true)
  })

  it('detects appendable responses and builds normalized append requests', () => {
    expect(canAppendReportResponse(createResponse([], { hasMore: true, nextCursor: ' cursor:2 ' }))).toBe(true)
    expect(canAppendReportResponse(createResponse([], { hasMore: true, nextCursor: '   ' }))).toBe(false)
    expect(canAppendReportResponse(createResponse([], { hasMore: false, nextCursor: 'cursor:2' }))).toBe(false)

    const request: ReportExecutionRequestDto = {
      parameters: { as_of_utc: '2026-04-08' },
      offset: 150,
      limit: 0,
      cursor: null,
    }

    expect(buildAppendRequest(request, ' cursor:2 ')).toEqual({
      ...request,
      offset: 0,
      limit: 1,
      cursor: 'cursor:2',
    })
    expect(buildAppendRequest({ parameters: {}, offset: 2 }, 'next').limit).toBe(500)
    expect(canAppendReportResponse(null)).toBe(false)
  })

  it('merges compatible paged responses and carries forward the next page metadata', () => {
    const current = createResponse([
      {
        rowKind: ReportRowKind.Detail,
        cells: [{ value: 'Riverfront Tower' }],
      },
    ], {
      hasMore: true,
      nextCursor: 'cursor:2',
      diagnostics: { source: 'current' },
    })

    const next = createResponse([
      {
        rowKind: ReportRowKind.Detail,
        cells: [{ value: 'Harbor View Plaza' }],
      },
    ], {
      total: 12,
      hasMore: false,
      nextCursor: null,
      diagnostics: { source: 'next' },
    })

    expect(mergePagedReportResponses(current, next)).toEqual({
      sheet: createSheet([
        {
          rowKind: ReportRowKind.Detail,
          cells: [{ value: 'Riverfront Tower' }],
        },
        {
          rowKind: ReportRowKind.Detail,
          cells: [{ value: 'Harbor View Plaza' }],
        },
      ]),
      offset: 0,
      limit: 50,
      total: 12,
      hasMore: false,
      nextCursor: null,
      diagnostics: { source: 'next' },
    })
  })

  it('throws when a paged append returns an incompatible sheet shape', () => {
    const current = createResponse([], {
      sheet: createSheet([], {
        columns: [{ code: 'name', title: 'Name', dataType: 'string' }],
      }),
    })
    const next = createResponse([], {
      sheet: createSheet([], {
        columns: [{ code: 'amount', title: 'Amount', dataType: 'number' }],
      }),
    })

    expect(() => mergePagedReportResponses(current, next)).toThrow(
      'Paged report append returned an incompatible sheet shape.',
    )
  })

  it('merges sparse compatible sheets using safe row and response defaults', () => {
    const headerRows = [{ rowKind: ReportRowKind.Header, cells: [] }]
    const withHeaders = mergePagedReportResponses(
      createResponse([], { sheet: createSheet([], { headerRows }) }),
      createResponse([], { sheet: createSheet([], { headerRows }) }),
    )
    expect(withHeaders.sheet.headerRows).toEqual(headerRows)
    expect(withHeaders.sheet.headerRows).not.toBe(headerRows)

    const current = {
      sheet: { columns: undefined, rows: undefined, meta: undefined, headerRows: undefined },
      offset: 0,
      limit: 50,
      total: undefined,
      hasMore: true,
      nextCursor: 'old',
      diagnostics: { source: 'current' },
    } as never
    const next = {
      sheet: { columns: undefined, rows: undefined, meta: undefined, headerRows: undefined },
      offset: 0,
      limit: 50,
      total: undefined,
      hasMore: false,
      nextCursor: ' ',
      diagnostics: null,
    } as never

    expect(mergePagedReportResponses(current, next)).toEqual({
      sheet: { columns: [], rows: [], meta: null, headerRows: null },
      offset: 0,
      limit: 50,
      total: 0,
      hasMore: false,
      nextCursor: null,
      diagnostics: { source: 'current' },
    })
  })
})
