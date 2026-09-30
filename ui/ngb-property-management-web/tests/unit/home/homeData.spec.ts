import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  buildOpenItemsPath: vi.fn(() => '/receivables/open-items'),
  buildReconciliationPath: vi.fn(() => '/receivables/reconciliation'),
  buildReportPageUrl: vi.fn((code: string) => `/reports/${code}`),
}))

vi.mock('../../../src/router/pmRoutePaths', () => ({
  buildPmOpenItemsPath: mocks.buildOpenItemsPath,
  buildPmReconciliationPath: mocks.buildReconciliationPath,
}))

vi.mock('@ngbplatform/ui', () => ({
  buildDashboardMonthWindow: (asOf: Date, count: number) => {
    const dates = Array.from({ length: count }, (_, index) =>
      new Date(Date.UTC(asOf.getUTCFullYear(), asOf.getUTCMonth() - count + index + 1, 1)))
    return {
      labels: dates.map((date) => date.toISOString().slice(0, 7)),
      monthKeys: dates.map((date) => date.toISOString().slice(0, 7)),
      pointDates: dates,
    }
  },
  buildDocumentFullPageUrl: (type: string, id: string) => `/documents/${type}/${id}`,
  buildReportPageUrl: mocks.buildReportPageUrl,
  formatDashboardMonthChip: (value: string | null | undefined) => value ? `chip:${value}` : null,
  formatDashboardMonthLabel: (value: string) => `label:${value}`,
  httpGet: mocks.httpGet,
  isGuidString: (value: unknown) => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value ?? '')),
  parseDashboardUtcDateOnly: (value: unknown) => {
    const text = String(value ?? '')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return null
    const date = new Date(`${text}T00:00:00Z`)
    return Number.isNaN(date.getTime()) ? null : date
  },
  toDashboardUtcMonthKey: (date: Date) => date.toISOString().slice(0, 7),
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

const partyId = '11111111-1111-4111-8111-111111111111'
const propertyId = '22222222-2222-4222-8222-222222222222'

function dashboardResponse() {
  return {
    asOfUtc: '2026-08-23',
    warnings: ['Late source'],
    portfolio: {
      buildingCount: 2,
      totalUnits: 10,
      occupiedUnits: 7,
      vacantUnits: 3,
      occupancyPercent: 70,
      futureOccupiedUnits: 8,
      futureOccupancyPercent: 80,
    },
    leases: {
      expiring30Count: 1,
      upcomingMoveInCount: 1,
      upcomingMoveOutCount: 0,
      events: [{
        kind: 'Move-in',
        date: '2026-08-24',
        leaseId: 'lease-1',
        leaseDisplay: 'Lease 1',
        propertyDisplay: 'North Building',
      }],
    },
    receivables: {
      totalOpenItemsNet: 500,
      totalDiff: -20,
      rowCount: 3,
      mismatchRowCount: 1,
      currentMonthBilled: 300,
      currentMonthCollected: 250,
      mismatches: [{
        partyId,
        propertyId,
        leaseId: 'lease-1',
        leaseDisplay: 'Lease 1',
        propertyDisplay: 'North Building',
        rowKind: 'MissingLedger',
        diff: -20,
      }],
    },
    maintenance: {
      openItemCount: 2,
      overdueCount: 1,
      items: [
        {
          requestId: 'request-1',
          workOrderId: 'work-1',
          queueState: 'WorkOrdered',
          subject: 'Repair lift',
          requestDisplay: 'MR-1',
          propertyDisplay: 'North Building',
          requestedAtUtc: '2026-08-01',
          dueByUtc: '2026-08-10',
          agingDays: 22,
          assignedTo: 'Alex',
        },
        {
          requestId: 'request-2',
          queueState: 'Requested',
          subject: 'Replace lamp',
          requestDisplay: 'MR-2',
          propertyDisplay: 'South Building',
          requestedAtUtc: '2026-08-22',
          agingDays: 1,
        },
      ],
      aging: { days0To3: 1, days4To7: 0, days8To14: 0, days15Plus: 1 },
    },
    periods: {
      pendingCloseCount: 2,
      lastClosedPeriod: '2026-06',
      nextClosablePeriod: '2026-07',
      firstGapPeriod: null,
    },
    occupancyTrend: [
      { month: '2026-07-01', occupiedUnits: 6, vacantUnits: 4 },
      { month: '2026-08-01', occupiedUnits: 7, vacantUnits: 3 },
    ],
    collectionsTrend: [
      { month: '2026-07-01', billed: 200, collected: 180 },
      { month: '2026-08-01', billed: 300, collected: 250 },
    ],
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.httpGet.mockResolvedValue(dashboardResponse())
})

describe('property-management home dashboard data', () => {
  it('maps the single aggregate response to dashboard cards, routes, and charts', async () => {
    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(mocks.httpGet).toHaveBeenCalledOnce()
    expect(mocks.httpGet).toHaveBeenCalledWith('/api/dashboard', { asOfUtc: '2026-08-23' })
    expect(dashboard.warnings).toEqual(['Late source'])
    expect(dashboard.monthLabel).toBe('label:2026-08')
    expect(dashboard.portfolio).toMatchObject({ buildingCount: 2, occupancyPercent: 70 })
    expect(dashboard.leases.events[0]).toMatchObject({ route: '/documents/pm.lease/lease-1' })
    expect(dashboard.receivables.mismatches[0]?.route).toContain(`partyId=${encodeURIComponent(partyId)}`)
    expect(dashboard.receivables.mismatches[0]?.route).toContain(`propertyId=${encodeURIComponent(propertyId)}`)
    expect(dashboard.maintenance.items).toEqual([
      expect.objectContaining({ queueState: 'Work ordered', route: '/documents/pm.work_order/work-1' }),
      expect.objectContaining({ queueState: 'Requested', route: '/documents/pm.maintenance_request/request-2' }),
    ])
    expect(dashboard.maintenance.agingBuckets.map((item) => item.value)).toEqual([1, 0, 0, 1])
    expect(dashboard.periods).toEqual({
      pendingCloseCount: 2,
      lastClosedPeriod: 'chip:2026-06',
      nextClosablePeriod: 'chip:2026-07',
      firstGapPeriod: null,
    })
    expect(dashboard.charts.collections.series[0]?.values).toEqual([200, 300])
    expect(dashboard.charts.occupancy.series[1]?.values).toEqual([4, 3])
  })

  it('uses bounded zero-filled chart fallbacks for sparse aggregate responses', async () => {
    const response = dashboardResponse()
    response.warnings = []
    response.occupancyTrend = []
    response.collectionsTrend = []
    response.leases.events = []
    response.receivables.mismatches = []
    response.maintenance.items = []
    response.periods = { pendingCloseCount: 0, lastClosedPeriod: null, nextClosablePeriod: null, firstGapPeriod: null }
    mocks.httpGet.mockResolvedValue(response)

    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(dashboard.charts.collections.labels).toHaveLength(12)
    expect(dashboard.charts.collections.series[0]?.values).toEqual(Array.from({ length: 12 }, () => 0))
    expect(dashboard.charts.occupancy.series[1]?.values).toEqual(Array.from({ length: 12 }, () => 0))
    expect(dashboard.leases.events).toEqual([])
    expect(dashboard.receivables.mismatches).toEqual([])
    expect(dashboard.maintenance.items).toEqual([])
  })

  it('forwards cancellation to the aggregate request', async () => {
    const controller = new AbortController()

    await loadHomeDashboard('2026-08-23', controller.signal)

    expect(mocks.httpGet).toHaveBeenCalledWith(
      '/api/dashboard',
      { asOfUtc: '2026-08-23' },
      { signal: controller.signal },
    )
  })

  it('normalizes omitted collections, warnings, invalid months, and invalid route ids', async () => {
    const response = dashboardResponse() as any
    response.warnings = undefined
    response.occupancyTrend = [{ month: 'invalid', occupiedUnits: 1, vacantUnits: 2 }]
    response.collectionsTrend = undefined
    response.leases.events = undefined
    response.receivables.mismatches = [{
      partyId: 'invalid', propertyId: 'invalid', leaseId: 'lease-1', leaseDisplay: 'Lease',
      propertyDisplay: 'Property', rowKind: 'Mismatch', diff: 1,
    }]
    response.maintenance.items = undefined
    mocks.httpGet.mockResolvedValue(response)

    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(dashboard.warnings).toEqual([])
    expect(dashboard.charts.occupancy.labels).toEqual(['invalid'])
    expect(dashboard.charts.collections.labels).toHaveLength(12)
    expect(dashboard.leases.events).toEqual([])
    expect(dashboard.receivables.mismatches[0]?.route).toBe('/receivables/open-items?leaseId=lease-1')
    expect(dashboard.maintenance.items).toEqual([])
  })

  it('normalizes omitted occupancy and mismatch collections at the API boundary', async () => {
    const response = dashboardResponse() as any
    response.occupancyTrend = undefined
    response.receivables.mismatches = undefined
    mocks.httpGet.mockResolvedValue(response)

    const dashboard = await loadHomeDashboard('2026-08-23')

    expect(dashboard.charts.occupancy.labels).toHaveLength(12)
    expect(dashboard.charts.occupancy.series[0]?.values).toEqual(Array.from({ length: 12 }, () => 0))
    expect(dashboard.receivables.mismatches).toEqual([])
  })

  it('rejects invalid as-of values before issuing a request', async () => {
    await expect(loadHomeDashboard('not-a-date')).rejects.toThrow('Select a valid as-of date.')
    expect(mocks.httpGet).not.toHaveBeenCalled()
  })
})
