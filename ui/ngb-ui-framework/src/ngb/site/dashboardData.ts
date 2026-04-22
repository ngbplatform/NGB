import { getPeriodClosingCalendar } from '../accounting/periodClosingApi'
import type { PeriodClosingCalendarDto } from '../accounting/periodClosingTypes'
import { isReferenceValue } from '../metadata/entityModel'
import { ReportRowKind, type ReportCellDto, type ReportExecutionResponseDto, type ReportSheetRowDto } from '../reporting/types'
import { normalizeDateOnlyValue } from '../utils/dateValues'
import { toErrorMessage } from '../utils/errorMessage'

const DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/
const MONTH_KEY_RE = /^(\d{4})-(\d{2})$/
const DEFAULT_PAGE_LIMIT = 200
const MAX_PAGED_REQUESTS = 20

export type DashboardCaptureResult<T> = {
  value: T | null
  warning: string | null
}

export type DashboardMonthWindow = {
  labels: string[]
  monthKeys: string[]
  pointDates: Date[]
}

export type DashboardPeriodClosingSummary = {
  pendingCloseCount: number
  lastClosedPeriod: string | null
  nextClosablePeriod: string | null
  firstGapPeriod: string | null
}

export type DashboardDocumentLike = {
  id: string
  status?: unknown
  display?: string | null
  payload?: {
    fields?: Record<string, unknown> | null
  } | null
}

export type DashboardPageResponse<TItem> = {
  items?: TItem[] | null
  total?: number | null
}

export type DashboardDocumentPageLoader<TDocument> = (
  documentType: string,
  options: {
    offset: number
    limit: number
    filters?: Record<string, string>
  },
) => Promise<DashboardPageResponse<TDocument>>

export async function captureDashboardValue<T>(
  label: string,
  factory: () => Promise<T>,
): Promise<DashboardCaptureResult<T>> {
  try {
    return { value: await factory(), warning: null }
  } catch (error) {
    return { value: null, warning: `${label}: ${toErrorMessage(error, 'Request failed.')}` }
  }
}

export function parseDashboardUtcDateOnly(value: string | null | undefined): Date | null {
  const normalized = normalizeDateOnlyValue(value)
  if (!normalized) return null

  const match = DATE_ONLY_RE.exec(normalized)
  if (!match) return null

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(Date.UTC(year, month - 1, day))
  return Number.isNaN(date.getTime()) ? null : date
}

export function toDashboardUtcDateOnly(date: Date): string {
  const year = date.getUTCFullYear()
  const month = String(date.getUTCMonth() + 1).padStart(2, '0')
  const day = String(date.getUTCDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function startOfDashboardUtcMonth(date: Date): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))
}

export function endOfDashboardUtcMonth(date: Date): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0))
}

export function addDashboardUtcMonths(date: Date, offset: number): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + offset, date.getUTCDate()))
}

export function addDashboardUtcDays(date: Date, offset: number): Date {
  return new Date(date.getTime() + offset * 24 * 60 * 60 * 1000)
}

export function toDashboardUtcMonthKey(date: Date): string {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`
}

export function formatDashboardMonthLabel(monthKey: string): string {
  const match = MONTH_KEY_RE.exec(String(monthKey ?? '').trim())
  if (!match) return monthKey

  const year = Number(match[1])
  const month = Number(match[2])
  return new Date(Date.UTC(year, month - 1, 1)).toLocaleString(undefined, {
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

export function formatDashboardMonthChip(dateOnly: string | null | undefined): string | null {
  const date = parseDashboardUtcDateOnly(dateOnly)
  if (!date) return null

  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)).toLocaleString(undefined, {
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

export function compareDashboardUtcDateOnly(left: string | null | undefined, right: string | null | undefined): number {
  const leftDate = parseDashboardUtcDateOnly(left)
  const rightDate = parseDashboardUtcDateOnly(right)
  if (!leftDate && !rightDate) return 0
  if (!leftDate) return 1
  if (!rightDate) return -1
  return leftDate.getTime() - rightDate.getTime()
}

export function isDashboardUtcDateWithinRange(
  dateOnly: string | null | undefined,
  fromInclusive: Date,
  toInclusive: Date,
): boolean {
  const value = parseDashboardUtcDateOnly(dateOnly)
  if (!value) return false
  return value.getTime() >= fromInclusive.getTime() && value.getTime() <= toInclusive.getTime()
}

export function formatDashboardMoney(value: number): string {
  const normalized = Number.isFinite(value) ? value : 0
  return normalized.toLocaleString(undefined, {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  })
}

export function formatDashboardMoneyCompact(value: number): string {
  const normalized = Number.isFinite(value) ? value : 0
  const abs = Math.abs(normalized)
  if (abs >= 1_000_000) return `${normalized < 0 ? '-' : ''}$${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${normalized < 0 ? '-' : ''}$${(abs / 1_000).toFixed(1)}K`
  return formatDashboardMoney(normalized)
}

export function formatDashboardPercent(value: number, digits = 1): string {
  if (!Number.isFinite(value)) return '0%'
  return `${value.toFixed(digits)}%`
}

export function formatDashboardCount(value: number): string {
  const normalized = Number.isFinite(value) ? value : 0
  return normalized.toLocaleString()
}

export function toDashboardMoney(value: unknown): number {
  const numeric = Number(value ?? 0)
  return Number.isFinite(numeric) ? numeric : 0
}

export function toDashboardInteger(value: unknown): number {
  const numeric = Number(value ?? 0)
  return Number.isFinite(numeric) ? Math.round(numeric) : 0
}

export function dashboardFieldValue(document: DashboardDocumentLike, key: string): unknown {
  return (document.payload?.fields ?? {})[key]
}

export function dashboardFieldMoney(document: DashboardDocumentLike, key: string): number {
  return toDashboardMoney(dashboardFieldValue(document, key))
}

export function dashboardFieldDateOnly(document: DashboardDocumentLike, key: string): string | null {
  return normalizeDateOnlyValue(dashboardFieldValue(document, key))
}

export function dashboardFieldDisplay(document: DashboardDocumentLike, key: string): string | null {
  const value = dashboardFieldValue(document, key)
  if (isReferenceValue(value)) return value.display

  const raw = String(value ?? '').trim()
  return raw.length > 0 ? raw : null
}

export function isPostedDashboardDocument(document: DashboardDocumentLike, postedStatus = 2): boolean {
  return Number(document.status) === postedStatus
}

export async function fetchAllPagedDashboardDocuments<TDocument>(
  loadPage: DashboardDocumentPageLoader<TDocument>,
  documentType: string,
  opts?: {
    periodFrom?: string | null
    periodTo?: string | null
    filters?: Record<string, string>
    limit?: number
    maxPages?: number
  },
): Promise<TDocument[]> {
  const limit = opts?.limit ?? DEFAULT_PAGE_LIMIT
  const maxPages = opts?.maxPages ?? MAX_PAGED_REQUESTS
  const filters: Record<string, string> = {
    deleted: 'active',
    ...(opts?.filters ?? {}),
  }

  if (opts?.periodFrom) filters.periodFrom = opts.periodFrom
  if (opts?.periodTo) filters.periodTo = opts.periodTo

  const items: TDocument[] = []
  let offset = 0
  let pageCount = 0
  let total: number | null = null

  while (pageCount < maxPages) {
    const page = await loadPage(documentType, { offset, limit, filters })
    items.push(...(page.items ?? []))
    total = typeof page.total === 'number' ? page.total : total
    pageCount += 1

    const loaded = items.length
    const reachedTotal = total != null ? loaded >= total : (page.items?.length ?? 0) < limit
    if (reachedTotal || (page.items?.length ?? 0) === 0) break
    offset += limit
  }

  return items
}

export function isDashboardReportRowKind(row: ReportSheetRowDto, kind: ReportRowKind): boolean {
  return row.rowKind === kind
    || String(row.rowKind).toLowerCase() === String(ReportRowKind[kind]).toLowerCase()
}

export function dashboardReportColumnIndexMap(response: ReportExecutionResponseDto): Map<string, number> {
  return new Map((response.sheet.columns ?? []).map((column, index) => [String(column.code ?? '').trim(), index]))
}

export function dashboardReportCellByCode(
  row: ReportSheetRowDto,
  columns: Map<string, number>,
  code: string,
): ReportCellDto | null {
  const index = columns.get(code)
  if (index == null) return null
  return row.cells[index] ?? null
}

export function dashboardReportCellDisplay(
  row: ReportSheetRowDto,
  columns: Map<string, number>,
  code: string,
): string {
  return String(dashboardReportCellByCode(row, columns, code)?.display ?? '').trim()
}

export function dashboardReportCellNumber(
  row: ReportSheetRowDto,
  columns: Map<string, number>,
  code: string,
): number {
  const cell = dashboardReportCellByCode(row, columns, code)
  if (!cell) return 0
  return toDashboardMoney(cell.value ?? cell.display)
}

export function buildDashboardMonthWindow(asOfDate: Date, count: number): DashboardMonthWindow {
  const monthKeys: string[] = []
  const labels: string[] = []
  const pointDates: Date[] = []

  for (let index = count - 1; index >= 0; index -= 1) {
    const monthDate = startOfDashboardUtcMonth(addDashboardUtcMonths(asOfDate, -index))
    const monthKey = toDashboardUtcMonthKey(monthDate)
    monthKeys.push(monthKey)
    labels.push(new Date(Date.UTC(monthDate.getUTCFullYear(), monthDate.getUTCMonth(), 1)).toLocaleString(undefined, {
      month: 'short',
      timeZone: 'UTC',
    }))

    const currentMonthKey = toDashboardUtcMonthKey(asOfDate)
    pointDates.push(monthKey === currentMonthKey ? asOfDate : endOfDashboardUtcMonth(monthDate))
  }

  return { labels, monthKeys, pointDates }
}

export async function loadDashboardPeriodClosingSummary(
  asOf: string,
  loadCalendar: (year: number) => Promise<PeriodClosingCalendarDto> = getPeriodClosingCalendar,
): Promise<DashboardPeriodClosingSummary> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Invalid as-of date.')

  const calendar = await loadCalendar(asOfDate.getUTCFullYear())
  return {
    pendingCloseCount: (calendar.months ?? []).filter((month) => month.hasActivity && !month.isClosed).length,
    lastClosedPeriod: calendar.latestContiguousClosedPeriod ?? calendar.latestClosedPeriod ?? null,
    nextClosablePeriod: calendar.nextClosablePeriod ?? null,
    firstGapPeriod: calendar.firstGapPeriod ?? null,
  }
}
