import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  executeReport: vi.fn(),
  fetchDocuments: vi.fn(),
  getReconciliation: vi.fn(),
  loadPeriods: vi.fn(),
  buildOpenItemsPath: vi.fn(() => '/receivables/open-items'),
  buildReconciliationPath: vi.fn(() => '/receivables/reconciliation?range=12'),
  buildReportPageUrl: vi.fn((code: string) => `/reports/${code}`),
  buildMonthWindow: vi.fn(),
  fieldDateOnly: vi.fn(),
  parseDate: vi.fn(),
}))

vi.mock('../../../src/api/clients/receivables', () => ({
  getReceivablesReconciliation: mocks.getReconciliation,
}))

vi.mock('../../../src/router/pmRoutePaths', () => ({
  buildPmOpenItemsPath: mocks.buildOpenItemsPath,
  buildPmReconciliationPath: mocks.buildReconciliationPath,
}))

vi.mock('@ngbplatform/ui', () => {
  const parseDate = (value: unknown): Date | null => {
    const normalized = String(value ?? '').trim()
    if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) return null
    const date = new Date(`${normalized}T00:00:00Z`)
    return Number.isNaN(date.getTime()) ? null : date
  }
  const dateOnly = (date: Date) => date.toISOString().slice(0, 10)
  const monthKey = (date: Date) => date.toISOString().slice(0, 7)
  const monthWindow = (asOf: Date, count: number) => {
    const pointDates = Array.from({ length: count }, (_, index) =>
      new Date(Date.UTC(asOf.getUTCFullYear(), asOf.getUTCMonth() - count + index + 1, 1)))
    return {
      pointDates,
      monthKeys: pointDates.map(monthKey),
      labels: pointDates.map((date) => monthKey(date)),
    }
  }
  mocks.buildMonthWindow.mockImplementation(monthWindow)
  mocks.fieldDateOnly.mockImplementation((document: any, key: string) => document.fields?.[key]?.date ?? null)
  mocks.parseDate.mockImplementation(parseDate)

  return {
    ReportRowKind: { Detail: 'Detail', Total: 'Total' },
    addDashboardUtcDays: (date: Date, days: number) => new Date(date.getTime() + days * 86_400_000),
    buildDashboardMonthWindow: mocks.buildMonthWindow,
    buildDocumentFullPageUrl: (type: string, id: string) => `/documents/${type}/${id}`,
    buildReportPageUrl: mocks.buildReportPageUrl,
    captureDashboardValue: async (warning: string, load: () => Promise<unknown>) => {
      try {
        return { value: await load(), warning: null }
      } catch {
        return { value: null, warning }
      }
    },
    compareDashboardUtcDateOnly: (left: string, right: string) => left.localeCompare(right),
    dashboardFieldDateOnly: mocks.fieldDateOnly,
    dashboardFieldDisplay: (document: any, key: string) => document.fields?.[key]?.display ?? null,
    dashboardFieldMoney: (document: any, key: string) => document.fields?.[key]?.money ?? 0,
    dashboardReportCellByCode: (row: any, _columns: unknown, code: string) => row.cells?.[code],
    dashboardReportCellDisplay: (row: any, _columns: unknown, code: string) => row.cells?.[code]?.display ?? '',
    dashboardReportCellNumber: (row: any, _columns: unknown, code: string) => Number(row.cells?.[code]?.value ?? 0),
    dashboardReportColumnIndexMap: () => ({}),
    executeReport: mocks.executeReport,
    fetchAllPagedDashboardDocuments: (_getter: unknown, type: string, query?: unknown) => mocks.fetchDocuments(type, query),
    formatDashboardMonthChip: (value: string | null) => value ? `chip:${value}` : null,
    formatDashboardMonthLabel: (value: string) => `label:${value}`,
    getDocumentPage: vi.fn(),
    isDashboardReportRowKind: (row: any, kind: string) => row.kind === kind,
    isDashboardUtcDateWithinRange: (value: string | null, from: Date, to: Date) => {
      const parsed = parseDate(value)
      return parsed !== null && parsed >= from && parsed <= to
    },
    isPostedDashboardDocument: (document: any) => document.posted === true,
    loadDashboardPeriodClosingSummary: mocks.loadPeriods,
    parseDashboardUtcDateOnly: mocks.parseDate,
    resolveReportCellActionUrl: (action: any) => action?.url ?? null,
    startOfDashboardUtcMonth: (date: Date) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)),
    toDashboardInteger: (value: unknown) => Math.trunc(Number(value) || 0),
    toDashboardUtcDateOnly: dateOnly,
    toDashboardUtcMonthKey: monthKey,
    isGuidString: (value: unknown) => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value ?? '')),
  }
})

import { loadHomeDashboard } from '../../../src/home/homeData'

const guidA = '11111111-1111-4111-8111-111111111111'
const guidB = '22222222-2222-4222-8222-222222222222'

function reportRow(kind: string, values: Record<string, unknown>) {
  return {
    kind,
    cells: Object.fromEntries(Object.entries(values).map(([key, value]) => [
      key,
      typeof value === 'object' && value !== null ? value : { value, display: String(value) },
    ])),
  }
}

function occupancyReport(asOf: string) {
  const detail = reportRow('Detail', {
    total_units: 10,
    occupied_units: 7,
    vacant_units: 3,
    occupancy_percent: 70,
  })
  if (asOf === '2026-03-01') {
    return { sheet: { rows: [detail, reportRow('Other', {})] } }
  }
  return {
    total: 2,
    sheet: {
      rows: [
        detail,
        reportRow('Total', {
          total_units: asOf === '2026-09-22' ? 20 : 10,
          occupied_units: asOf === '2026-09-22' ? 16 : 7,
          vacant_units: asOf === '2026-09-22' ? 4 : 3,
          occupancy_percent: asOf === '2026-09-22' ? 80 : 70,
        }),
      ],
    },
  }
}

function maintenanceRow(
  queueState: string | null,
  aging: number | string,
  options: { subject?: string; property?: string; building?: string; workUrl?: string; requestUrl?: string; useDisplayAging?: boolean } = {},
) {
  return reportRow('Detail', {
    queue_state: { display: queueState ?? '' },
    subject: { display: options.subject ?? '' },
    request: { display: '', action: options.requestUrl ? { url: options.requestUrl } : null },
    property: { display: options.property ?? '' },
    building: { display: options.building ?? '' },
    requested_at_utc: { display: '' },
    due_by_utc: { display: '' },
    aging_days: options.useDisplayAging ? { display: String(aging) } : { value: aging, display: String(aging) },
    assigned_to: { display: '' },
    work_order: { action: options.workUrl ? { url: options.workUrl } : null },
  })
}

function postedDocument(id: string, fields: Record<string, unknown>, display?: string) {
  return { id, display, posted: true, fields }
}

beforeEach(() => {
  vi.clearAllMocks()

  mocks.executeReport.mockImplementation(async (code: string, request: any) => {
    if (code === 'pm.occupancy.summary') return occupancyReport(request.parameters.as_of_utc)
    if (request.filters) return { total: 2, sheet: { rows: [reportRow('Detail', {})] } }
    return {
      total: 8,
      sheet: {
        rows: [
          maintenanceRow('Requested', 2, { subject: 'Request A', property: 'Property A', workUrl: '/work/1' }),
          maintenanceRow('WorkOrdered', 6, { building: 'Building B', requestUrl: '/request/2', useDisplayAging: true }),
          maintenanceRow('Overdue', 9),
          maintenanceRow('Overdue', 18),
          maintenanceRow('Other', 1),
          maintenanceRow(null, 5),
          maintenanceRow('Requested', 3),
          reportRow('Other', {}),
        ],
      },
    }
  })

  mocks.fetchDocuments.mockImplementation(async (type: string) => {
    if (type === 'pm.lease') {
      return [
        postedDocument('lease-move', {
          start_on_utc: { date: '2026-08-24' },
          end_on_utc: { date: '2026-08-30' },
          property_id: { display: 'Building One' },
        }, 'Lease Move'),
        postedDocument('lease-expiring', {
          start_on_utc: { date: '2020-01-01' },
          end_on_utc: { date: '2026-09-15' },
          property_id: { display: null },
        }),
        postedDocument('lease-fallback', {
          start_on_utc: { date: '2026-08-25' },
          end_on_utc: { date: '2026-08-31' },
          property_id: { display: null },
        }),
        postedDocument('lease-invalid', {
          start_on_utc: { date: 'bad' },
          end_on_utc: { date: null },
        }),
        { id: 'lease-draft', posted: false, fields: {} },
      ]
    }

    const dateField: Record<string, string> = {
      'pm.rent_charge': 'due_on_utc',
      'pm.receivable_charge': 'due_on_utc',
      'pm.late_fee_charge': 'due_on_utc',
      'pm.receivable_payment': 'received_on_utc',
      'pm.receivable_returned_payment': 'returned_on_utc',
    }
    const key = dateField[type]
    return [
      postedDocument(`${type}-current`, { [key]: { date: '2026-08-10' }, amount: { money: 100 } }),
      postedDocument(`${type}-outside`, { [key]: { date: '2020-01-01' }, amount: { money: 999 } }),
      postedDocument(`${type}-invalid`, { [key]: { date: 'invalid' }, amount: { money: 999 } }),
      { id: `${type}-draft`, posted: false, fields: {} },
    ]
  })

  mocks.getReconciliation.mockResolvedValue({
    totalOpenItemsNet: 500,
    totalDiff: -20,
    rowCount: 9,
    mismatchRowCount: 8,
    rows: [
      { rowKind: 'Matched', diff: 0, leaseId: 'matched' },
      { rowKind: 'MissingLedger', diff: -100, leaseId: 'lease-a', leaseDisplay: ' Lease A ', propertyDisplay: ' Property A ', partyId: guidA, propertyId: guidB },
      { rowKind: 'MissingOpenItem', diff: 90, leaseId: 'lease-b', propertyId: 'invalid' },
      { rowKind: 'Malformed', diff: 80, leaseId: undefined, propertyId: undefined },
      ...Array.from({ length: 6 }, (_, index) => ({ rowKind: `Mismatch${index}`, diff: index + 1, leaseId: `lease-${index}` })),
    ],
  })
  mocks.loadPeriods.mockResolvedValue({
    pendingCloseCount: 2,
    lastClosedPeriod: '2026-06',
    nextClosablePeriod: '2026-07',
    firstGapPeriod: null,
  })
})

describe('property-management home dashboard data', () => {
  it('builds a complete dashboard from reports and documents, including mismatch routes and aging buckets', async () => {
    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(dashboard.warnings).toEqual([])
    expect(dashboard.monthKey).toBe('2026-08')
    expect(dashboard.monthLabel).toBe('label:2026-08')
    expect(dashboard.portfolio).toEqual({
      buildingCount: 2,
      totalUnits: 10,
      occupiedUnits: 7,
      vacantUnits: 3,
      occupancyPercent: 70,
      futureOccupiedUnits: 16,
      futureOccupancyPercent: 80,
    })
    expect(dashboard.leases).toMatchObject({ expiring30Count: 3, upcomingMoveInCount: 2, upcomingMoveOutCount: 2 })
    expect(dashboard.leases.events).toEqual([
      expect.objectContaining({ kind: 'Move-in', leaseDisplay: 'Lease Move', propertyDisplay: 'Building One' }),
      expect.objectContaining({ kind: 'Move-in', leaseDisplay: 'lease-fallback', propertyDisplay: 'Property' }),
      expect.objectContaining({ kind: 'Move-out', leaseDisplay: 'Lease Move', propertyDisplay: 'Building One' }),
      expect.objectContaining({ kind: 'Move-out', leaseDisplay: 'lease-fallback', propertyDisplay: 'Property' }),
    ])
    expect(dashboard.receivables).toMatchObject({
      totalOpenItemsNet: 500,
      totalDiff: -20,
      rowCount: 9,
      mismatchRowCount: 8,
      currentMonthBilled: 300,
      currentMonthCollected: 0,
    })
    expect(dashboard.receivables.mismatches).toHaveLength(6)
    expect(dashboard.receivables.mismatches[0]).toMatchObject({ leaseDisplay: 'Lease A', propertyDisplay: 'Property A', diff: -100 })
    expect(dashboard.receivables.mismatches[1]).toMatchObject({ leaseDisplay: 'lease-b', propertyDisplay: 'invalid', diff: 90 })
    expect(dashboard.receivables.mismatches[0]?.route).toContain(`partyId=${encodeURIComponent(guidA)}`)
    expect(dashboard.receivables.mismatches[0]?.route).toContain(`propertyId=${encodeURIComponent(guidB)}`)
    expect(dashboard.receivables.mismatches[1]?.route).not.toContain('propertyId=')
    expect(dashboard.maintenance).toMatchObject({
      openItemCount: 8,
      overdueCount: 2,
      agingBuckets: [
        { label: '0-3 days', value: 3 },
        { label: '4-7 days', value: 2 },
        { label: '8-14 days', value: 1 },
        { label: '15+ days', value: 1 },
      ],
    })
    expect(dashboard.maintenance.items).toHaveLength(6)
    expect(dashboard.periods).toEqual({
      pendingCloseCount: 2,
      lastClosedPeriod: 'chip:2026-06',
      nextClosablePeriod: 'chip:2026-07',
      firstGapPeriod: null,
    })
    expect(dashboard.charts.collections.series[0]?.values.at(-1)).toBe(300)
    expect(dashboard.charts.collections.series[1]?.values.at(-1)).toBe(0)
    expect(dashboard.charts.occupancy.series[0]?.values).toHaveLength(12)
    expect(dashboard.charts.maintenanceAging.route).toBe('/reports/pm.maintenance.queue')
  })

  it('returns every safe fallback and warning when independent dashboard sources fail', async () => {
    mocks.executeReport.mockRejectedValue(new Error('reports offline'))
    mocks.fetchDocuments.mockRejectedValue(new Error('documents offline'))
    mocks.getReconciliation.mockRejectedValue(new Error('reconciliation offline'))
    mocks.loadPeriods.mockRejectedValue(new Error('periods offline'))

    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(dashboard.warnings).toEqual([
      'Occupancy summary is unavailable',
      'Lease analytics are unavailable',
      'Occupancy trend is unavailable',
      'Maintenance queue is unavailable',
      'Receivables reconciliation is unavailable',
      'Period closing status is unavailable',
      'Collections trend is unavailable',
    ])
    expect(dashboard.portfolio).toEqual({
      buildingCount: 0,
      totalUnits: 0,
      occupiedUnits: 0,
      vacantUnits: 0,
      occupancyPercent: 0,
      futureOccupiedUnits: 0,
      futureOccupancyPercent: 0,
    })
    expect(dashboard.leases.events).toEqual([])
    expect(dashboard.receivables).toMatchObject({ totalOpenItemsNet: 0, totalDiff: 0, rowCount: 0, mismatchRowCount: 0, mismatches: [] })
    expect(dashboard.maintenance.items).toEqual([])
    expect(dashboard.periods).toEqual({ pendingCloseCount: 0, lastClosedPeriod: null, nextClosablePeriod: null, firstGapPeriod: null })
    expect(dashboard.charts.collections.series[0]?.values).toEqual(Array.from({ length: 12 }, () => 0))
    expect(dashboard.charts.occupancy.series[1]?.values).toEqual(Array.from({ length: 12 }, () => 0))
  })

  it('normalizes sparse report payloads without totals or row arrays', async () => {
    let overdueHasRows = true
    mocks.executeReport.mockImplementation(async (code: string, request: any) => {
      if (code === 'pm.occupancy.summary') return { sheet: { rows: undefined } }
      if (request.filters) {
        return { sheet: { rows: overdueHasRows ? [reportRow('Detail', {}), reportRow('Other', {})] : undefined } }
      }
      return { sheet: { rows: undefined } }
    })
    mocks.fetchDocuments.mockResolvedValue([])
    mocks.getReconciliation.mockResolvedValue({
      totalOpenItemsNet: 0,
      totalDiff: 0,
      rowCount: 0,
      mismatchRowCount: 0,
      rows: undefined,
    })

    const withOverdueRow = await loadHomeDashboard('2026-08-23')
    expect(withOverdueRow.portfolio).toMatchObject({ buildingCount: 0, totalUnits: 0, occupancyPercent: 0, futureOccupancyPercent: 0 })
    expect(withOverdueRow.maintenance).toMatchObject({ openItemCount: 0, overdueCount: 1, items: [] })
    expect(withOverdueRow.receivables.mismatches).toEqual([])

    overdueHasRows = false
    const withoutRows = await loadHomeDashboard('2026-08-23')
    expect(withoutRows.maintenance.overdueCount).toBe(0)
  })

  it('uses month-key fallbacks when a dashboard window has no month keys', async () => {
    const original = mocks.buildMonthWindow.getMockImplementation()
    mocks.buildMonthWindow.mockImplementation((asOf: Date) => ({
      pointDates: [asOf],
      monthKeys: [],
      labels: [],
    }))

    try {
      const dashboard = await loadHomeDashboard('2026-08-23')
      expect(dashboard.charts.collections.labels).toEqual([])
      expect(dashboard.charts.collections.series[0]?.values).toEqual([])
      expect(dashboard.receivables.currentMonthBilled).toBe(0)
      expect(dashboard.receivables.currentMonthCollected).toBe(0)
      expect(mocks.buildReconciliationPath).toHaveBeenCalledWith('receivables', {
        fromMonth: '2026-08',
        toMonth: '2026-08',
        mode: 'Movement',
      })
      expect(mocks.fetchDocuments).toHaveBeenCalledWith('pm.rent_charge', {
        periodFrom: '2026-08-01',
        periodTo: '2026-08-23',
      })
    } finally {
      mocks.buildMonthWindow.mockImplementation(original!)
    }
  })

  it('isolates invalid dates returned by dependency parsing inside individual analytics loaders', async () => {
    const original = mocks.parseDate.getMockImplementation()
    let call = 0
    mocks.parseDate.mockImplementation((value: unknown) => {
      call += 1
      if (call === 1) return new Date('2026-08-23T00:00:00Z')
      if (call <= 4) return null
      return original!(value)
    })

    try {
      const dashboard = await loadHomeDashboard('2026-08-23')
      expect(dashboard.warnings).toEqual([
        'Lease analytics are unavailable',
        'Receivables reconciliation is unavailable',
        'Collections trend is unavailable',
      ])
    } finally {
      mocks.parseDate.mockImplementation(original!)
    }
  })

  it('rejects an invalid as-of value before issuing any request', async () => {
    await expect(loadHomeDashboard('not-a-date')).rejects.toThrow('Select a valid as-of date.')
    expect(mocks.executeReport).not.toHaveBeenCalled()
  })
})
