import { ReportRowKind, type ReportExecutionRequestDto, type ReportExecutionResponseDto, type ReportSheetDto, type ReportSheetRowDto } from './types'
import { stableEquals } from '../utils/stableValue'

function normalizeCursor(value: string | null | undefined): string | null {
  const normalized = String(value ?? '').trim()
  return normalized.length > 0 ? normalized : null
}

function isGrandTotalRow(row: ReportSheetRowDto | null | undefined): boolean {
  if (!row) return false
  if (row.rowKind === ReportRowKind.Total) return true
  return String(row.semanticRole ?? '').trim().toLowerCase() === 'grand_total'
}

export function countLoadedReportRows(sheet: ReportSheetDto | null | undefined): number {
  const rows = sheet?.rows ?? []
  return rows.reduce((count, row) => count + (isGrandTotalRow(row) ? 0 : 1), 0)
}

function cloneSheetWithRows(sheet: ReportSheetDto, rows: ReportSheetDto['rows']): ReportSheetDto {
  return {
    columns: [...(sheet.columns ?? [])],
    rows,
    meta: sheet.meta ?? null,
    headerRows: sheet.headerRows ? [...sheet.headerRows] : null,
  }
}

function areSheetsAppendCompatible(left: ReportSheetDto, right: ReportSheetDto): boolean {
  return stableEquals(left.columns ?? [], right.columns ?? [])
    && stableEquals(left.headerRows ?? [], right.headerRows ?? [])
}

export function canAppendReportResponse(response: ReportExecutionResponseDto | null | undefined): boolean {
  return !!response?.hasMore && !!normalizeCursor(response.nextCursor)
}

export function buildAppendRequest(baseRequest: ReportExecutionRequestDto, nextCursor: string): ReportExecutionRequestDto {
  return {
    ...baseRequest,
    offset: 0,
    limit: Math.max(1, baseRequest.limit ?? 500),
    cursor: normalizeCursor(nextCursor),
  }
}

export function mergePagedReportResponses(
  current: ReportExecutionResponseDto,
  next: ReportExecutionResponseDto,
): ReportExecutionResponseDto {
  if (!areSheetsAppendCompatible(current.sheet, next.sheet)) {
    throw new Error('Paged report append returned an incompatible sheet shape.')
  }

  const mergedRows = [...(current.sheet.rows ?? []), ...(next.sheet.rows ?? [])]

  return {
    sheet: cloneSheetWithRows(current.sheet, mergedRows),
    offset: current.offset,
    limit: current.limit,
    total: next.total ?? current.total ?? mergedRows.length,
    hasMore: next.hasMore,
    nextCursor: normalizeCursor(next.nextCursor),
    diagnostics: next.diagnostics ?? current.diagnostics,
  }
}
