import { describe, expect, it, vi } from 'vitest'

vi.mock('../../../../src/ngb/accounting/periodClosingApi', () => ({
  getPeriodClosingCalendar: vi.fn(),
}))

import {
  addDashboardUtcDays,
  addDashboardUtcMonths,
  buildDashboardMonthWindow,
  captureDashboardValue,
  compareDashboardUtcDateOnly,
  dashboardFieldDateOnly,
  dashboardFieldDisplay,
  dashboardFieldMoney,
  dashboardFieldValue,
  dashboardReportCellByCode,
  dashboardReportCellDisplay,
  dashboardReportCellNumber,
  dashboardReportColumnIndexMap,
  endOfDashboardUtcMonth,
  fetchAllPagedDashboardDocuments,
  formatDashboardCount,
  formatDashboardMoney,
  formatDashboardMoneyCompact,
  formatDashboardMonthChip,
  formatDashboardMonthLabel,
  formatDashboardPercent,
  isDashboardReportRowKind,
  isDashboardUtcDateWithinRange,
  isPostedDashboardDocument,
  loadDashboardPeriodClosingSummary,
  parseDashboardUtcDateOnly,
  startOfDashboardUtcMonth,
  toDashboardInteger,
  toDashboardMoney,
  toDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
} from '../../../../src/ngb/site/dashboardData'
import { ReportRowKind, type ReportExecutionResponseDto, type ReportSheetRowDto } from '../../../../src/ngb/reporting/types'
import { getPeriodClosingCalendar } from '../../../../src/ngb/accounting/periodClosingApi'

describe('dashboardData', () => {
  it('captures successful results and turns failures into warnings', async () => {
    await expect(captureDashboardValue('Receivables', async () => 42)).resolves.toEqual({
      value: 42,
      warning: null,
    })

    await expect(captureDashboardValue('Receivables', async () => {
      throw new Error('Ledger sync failed')
    })).resolves.toEqual({
      value: null,
      warning: 'Receivables: Ledger sync failed',
    })
  })

  it('normalizes dashboard dates, month labels, and rolling month windows', () => {
    const asOfDate = parseDashboardUtcDateOnly('2026-04-15')
    expect(asOfDate).not.toBeNull()

    expect(toDashboardUtcDateOnly(asOfDate!)).toBe('2026-04-15')
    expect(toDashboardUtcDateOnly(startOfDashboardUtcMonth(asOfDate!))).toBe('2026-04-01')
    expect(toDashboardUtcDateOnly(endOfDashboardUtcMonth(asOfDate!))).toBe('2026-04-30')
    expect(toDashboardUtcDateOnly(addDashboardUtcMonths(asOfDate!, -2))).toBe('2026-02-15')
    expect(toDashboardUtcDateOnly(addDashboardUtcDays(asOfDate!, 5))).toBe('2026-04-20')
    expect(toDashboardUtcMonthKey(asOfDate!)).toBe('2026-04')
    expect(formatDashboardMonthLabel('2026-04')).toBe('Apr 2026')
    expect(formatDashboardMonthChip('2026-04-15')).toBe('Apr 2026')
    expect(compareDashboardUtcDateOnly('2026-04-01', '2026-04-15')).toBeLessThan(0)
    expect(compareDashboardUtcDateOnly(null, '2026-04-15')).toBeGreaterThan(0)
    expect(isDashboardUtcDateWithinRange(
      '2026-04-15',
      new Date(Date.UTC(2026, 3, 1)),
      new Date(Date.UTC(2026, 3, 30)),
    )).toBe(true)

    const window = buildDashboardMonthWindow(asOfDate!, 3)
    expect(window.monthKeys).toEqual(['2026-02', '2026-03', '2026-04'])
    expect(window.labels).toEqual(['Feb', 'Mar', 'Apr'])
    expect(window.pointDates.map(toDashboardUtcDateOnly)).toEqual(['2026-02-28', '2026-03-31', '2026-04-15'])
  })

  it('formats dashboard values and extracts typed document fields', () => {
    const document = {
      id: 'invoice-1',
      status: 2,
      payload: {
        fields: {
          amount: '1250.25',
          units: '12.6',
          period: '2026-04-08',
          tenant: {
            id: 'tenant-1',
            display: 'Acme Resident',
          },
          note: '  Manual note  ',
        },
      },
    }

    expect(formatDashboardMoney(1250.25)).toBe('$1,250')
    expect(formatDashboardMoneyCompact(1_250_000)).toBe('$1.3M')
    expect(formatDashboardPercent(12.345, 2)).toBe('12.35%')
    expect(formatDashboardCount(12345)).toBe('12,345')
    expect(toDashboardMoney('oops')).toBe(0)
    expect(toDashboardInteger(document.payload.fields.units)).toBe(13)
    expect(dashboardFieldValue(document, 'tenant')).toEqual({ id: 'tenant-1', display: 'Acme Resident' })
    expect(dashboardFieldMoney(document, 'amount')).toBe(1250.25)
    expect(dashboardFieldDateOnly(document, 'period')).toBe('2026-04-08')
    expect(dashboardFieldDisplay(document, 'tenant')).toBe('Acme Resident')
    expect(dashboardFieldDisplay(document, 'note')).toBe('Manual note')
    expect(isPostedDashboardDocument(document)).toBe(true)
    expect(isPostedDashboardDocument({ ...document, status: 1 })).toBe(false)
  })

  it('loads paged dashboard documents until the reported total is reached', async () => {
    const loadPage = vi.fn()
      .mockResolvedValueOnce({
        items: [{ id: 'a' }, { id: 'b' }],
        total: 3,
      })
      .mockResolvedValueOnce({
        items: [{ id: 'c' }],
        total: 3,
      })

    await expect(fetchAllPagedDashboardDocuments(loadPage, 'pm.invoice', {
      periodFrom: '2026-04-01',
      periodTo: '2026-04-30',
      filters: {
        status: 'posted',
      },
      limit: 2,
    })).resolves.toEqual([{ id: 'a' }, { id: 'b' }, { id: 'c' }])

    expect(loadPage).toHaveBeenNthCalledWith(1, 'pm.invoice', {
      offset: 0,
      limit: 2,
      filters: {
        deleted: 'active',
        status: 'posted',
        periodFrom: '2026-04-01',
        periodTo: '2026-04-30',
      },
    })
    expect(loadPage).toHaveBeenNthCalledWith(2, 'pm.invoice', {
      offset: 2,
      limit: 2,
      filters: {
        deleted: 'active',
        status: 'posted',
        periodFrom: '2026-04-01',
        periodTo: '2026-04-30',
      },
    })
  })

  it('stops paged dashboard loading when maxPages is reached without a total', async () => {
    const loadPage = vi.fn()
      .mockResolvedValueOnce({ items: [{ id: 'a' }, { id: 'b' }] })
      .mockResolvedValueOnce({ items: [{ id: 'c' }, { id: 'd' }] })
      .mockResolvedValueOnce({ items: [{ id: 'e' }] })

    await expect(fetchAllPagedDashboardDocuments(loadPage, 'pm.invoice', {
      limit: 2,
      maxPages: 2,
    })).resolves.toEqual([{ id: 'a' }, { id: 'b' }, { id: 'c' }, { id: 'd' }])

    expect(loadPage).toHaveBeenCalledTimes(2)
  })

  it('reads report rows by column code and normalizes numeric accessors', () => {
    const detailRow = {
      rowKind: ReportRowKind.Detail,
      cells: [
        { display: ' Acme Resident ' },
        { value: 1550.5, display: '$1,550.50' },
      ],
    } satisfies ReportSheetRowDto

    const stringRowKind = {
      rowKind: 'Detail',
      cells: [],
    } as unknown as ReportSheetRowDto

    const response = {
      sheet: {
        columns: [
          { code: 'tenant', title: 'Tenant', dataType: 'string' },
          { code: 'balance', title: 'Balance', dataType: 'number' },
        ],
        rows: [detailRow],
      },
      offset: 0,
      limit: 50,
      hasMore: false,
    } satisfies ReportExecutionResponseDto

    const columns = dashboardReportColumnIndexMap(response)
    expect(columns.get('tenant')).toBe(0)
    expect(columns.get('balance')).toBe(1)
    expect(isDashboardReportRowKind(detailRow, ReportRowKind.Detail)).toBe(true)
    expect(isDashboardReportRowKind(stringRowKind, ReportRowKind.Detail)).toBe(true)
    expect(dashboardReportCellByCode(detailRow, columns, 'tenant')).toEqual({ display: ' Acme Resident ' })
    expect(dashboardReportCellDisplay(detailRow, columns, 'tenant')).toBe('Acme Resident')
    expect(dashboardReportCellNumber(detailRow, columns, 'balance')).toBe(1550.5)
    expect(dashboardReportCellNumber(detailRow, columns, 'missing')).toBe(0)
  })

  it('builds period-closing summaries and rejects invalid as-of dates', async () => {
    const loadCalendar = vi.fn().mockResolvedValue({
      months: [
        { month: 1, hasActivity: true, isClosed: true },
        { month: 2, hasActivity: true, isClosed: false },
        { month: 3, hasActivity: false, isClosed: false },
        { month: 4, hasActivity: true, isClosed: false },
      ],
      latestContiguousClosedPeriod: '2026-01',
      latestClosedPeriod: '2026-01',
      nextClosablePeriod: '2026-02',
      firstGapPeriod: '2026-02',
    })

    await expect(loadDashboardPeriodClosingSummary('2026-04-15', loadCalendar)).resolves.toEqual({
      pendingCloseCount: 2,
      lastClosedPeriod: '2026-01',
      nextClosablePeriod: '2026-02',
      firstGapPeriod: '2026-02',
    })
    expect(loadCalendar).toHaveBeenCalledWith(2026)

    await expect(loadDashboardPeriodClosingSummary('invalid-date', loadCalendar)).rejects.toThrow('Invalid as-of date.')
  })

  it('handles invalid dates, both comparison gaps, range boundaries, and non-finite formatting values', () => {
    const april = new Date(Date.UTC(2026, 3, 15))
    expect(parseDashboardUtcDateOnly(null)).toBeNull()
    expect(parseDashboardUtcDateOnly('2026-02-30')).toBeNull()
    expect(formatDashboardMonthLabel('not-a-month')).toBe('not-a-month')
    expect(formatDashboardMonthChip(undefined)).toBeNull()
    expect(compareDashboardUtcDateOnly(null, undefined)).toBe(0)
    expect(compareDashboardUtcDateOnly('2026-04-15', null)).toBeLessThan(0)
    expect(isDashboardUtcDateWithinRange(null, april, april)).toBe(false)
    expect(isDashboardUtcDateWithinRange('2026-04-01', april, new Date(Date.UTC(2026, 3, 30)))).toBe(false)
    expect(isDashboardUtcDateWithinRange('2026-05-01', new Date(Date.UTC(2026, 3, 1)), april)).toBe(false)

    expect(formatDashboardMoney(Number.NaN)).toBe('$0')
    expect(formatDashboardMoneyCompact(-1_250_000)).toBe('-$1.3M')
    expect(formatDashboardMoneyCompact(1_250)).toBe('$1.3K')
    expect(formatDashboardMoneyCompact(-1_250)).toBe('-$1.3K')
    expect(formatDashboardMoneyCompact(12)).toBe('$12')
    expect(formatDashboardMoneyCompact(Number.POSITIVE_INFINITY)).toBe('$0')
    expect(formatDashboardPercent(Number.NaN)).toBe('0%')
    expect(formatDashboardPercent(25)).toBe('25.0%')
    expect(formatDashboardCount(Number.POSITIVE_INFINITY)).toBe('0')
    expect(toDashboardMoney(null)).toBe(0)
    expect(toDashboardMoney('12.5')).toBe(12.5)
    expect(toDashboardInteger(null)).toBe(0)
    expect(toDashboardInteger('invalid')).toBe(0)
  })

  it('handles absent document payloads, blank displays, custom posted statuses, and sparse report data', () => {
    const emptyDocument = { id: 'empty', payload: null }
    expect(dashboardFieldValue(emptyDocument, 'missing')).toBeUndefined()
    expect(dashboardFieldDisplay(emptyDocument, 'missing')).toBeNull()
    expect(dashboardFieldDisplay({ id: 'blank', payload: { fields: { label: '   ' } } }, 'label')).toBeNull()
    expect(isPostedDashboardDocument({ id: 'custom', status: '7' }, 7)).toBe(true)

    const numericMismatch = { rowKind: ReportRowKind.Detail, cells: [] } satisfies ReportSheetRowDto
    expect(isDashboardReportRowKind(numericMismatch, ReportRowKind.Total)).toBe(false)

    const sparseResponse = {
      sheet: { columns: null, rows: [] },
      offset: 0,
      limit: 50,
      hasMore: false,
    } as unknown as ReportExecutionResponseDto
    expect(dashboardReportColumnIndexMap(sparseResponse).size).toBe(0)

    const unnamedResponse = {
      sheet: { columns: [{ code: null }], rows: [] },
      offset: 0,
      limit: 50,
      hasMore: false,
    } as unknown as ReportExecutionResponseDto
    expect(dashboardReportColumnIndexMap(unnamedResponse).get('')).toBe(0)

    const columns = new Map([['missing-cell', 2], ['display-only', 0]])
    const displayOnlyRow = { rowKind: ReportRowKind.Detail, cells: [{ display: '42.5' }] } satisfies ReportSheetRowDto
    expect(dashboardReportCellByCode(displayOnlyRow, columns, 'missing-cell')).toBeNull()
    expect(dashboardReportCellDisplay(displayOnlyRow, columns, 'missing-cell')).toBe('')
    expect(dashboardReportCellNumber(displayOnlyRow, columns, 'display-only')).toBe(42.5)
  })

  it('uses paging defaults and stops on an empty page even when the reported total is larger', async () => {
    const loadPage = vi.fn().mockResolvedValue({ items: null, total: 5 })

    await expect(fetchAllPagedDashboardDocuments(loadPage, 'pm.invoice')).resolves.toEqual([])
    expect(loadPage).toHaveBeenCalledWith('pm.invoice', {
      offset: 0,
      limit: 200,
      filters: { deleted: 'active' },
    })

    const skippedLoader = vi.fn()
    await expect(fetchAllPagedDashboardDocuments(skippedLoader, 'pm.invoice', { maxPages: 0 })).resolves.toEqual([])
    expect(skippedLoader).not.toHaveBeenCalled()

    const unknownTotalLoader = vi.fn().mockResolvedValue({ items: null })
    await expect(fetchAllPagedDashboardDocuments(unknownTotalLoader, 'pm.invoice')).resolves.toEqual([])
  })

  it('uses the default calendar loader and all nullable period-summary fallbacks', async () => {
    vi.mocked(getPeriodClosingCalendar)
      .mockResolvedValueOnce({
        months: null,
        latestContiguousClosedPeriod: null,
        latestClosedPeriod: '2025-12',
        nextClosablePeriod: null,
        firstGapPeriod: null,
      } as never)
      .mockResolvedValueOnce({
        months: [],
        latestContiguousClosedPeriod: null,
        latestClosedPeriod: null,
        nextClosablePeriod: null,
        firstGapPeriod: null,
      } as never)

    await expect(loadDashboardPeriodClosingSummary('2026-01-15')).resolves.toEqual({
      pendingCloseCount: 0,
      lastClosedPeriod: '2025-12',
      nextClosablePeriod: null,
      firstGapPeriod: null,
    })
    await expect(loadDashboardPeriodClosingSummary('2026-02-15')).resolves.toMatchObject({
      lastClosedPeriod: null,
    })
  })
})
