import { describe, expect, it } from 'vitest'

import {
  buildAppendRequest,
  canAppendReportResponse,
  countLoadedReportRows,
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
})
