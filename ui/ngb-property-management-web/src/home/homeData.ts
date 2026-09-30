import { buildPmOpenItemsPath, buildPmReconciliationPath } from '../router/pmRoutePaths'
import {
  buildDashboardMonthWindow,
  buildDocumentFullPageUrl,
  buildReportPageUrl,
  formatDashboardMonthChip,
  formatDashboardMonthLabel,
  httpGet,
  isGuidString,
  parseDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
} from '@ngbplatform/ui'

export type HomeTrendSeries = { label: string; color: string; values: number[] }
export type HomeLineChartData = { title: string; subtitle: string; labels: string[]; series: HomeTrendSeries[]; route: string }
export type HomeBarChartData = HomeLineChartData

export type HomeMaintenanceItem = {
  queueState: string
  subject: string
  requestDisplay: string
  propertyDisplay: string
  requestedAt: string | null
  dueBy: string | null
  agingDays: number
  assignedTo: string | null
  route: string | null
}

export type HomeLeaseEvent = {
  kind: 'Move-in' | 'Move-out'
  date: string
  leaseDisplay: string
  propertyDisplay: string
  route: string
}

export type HomeMismatchItem = {
  leaseDisplay: string
  propertyDisplay: string
  rowKind: string
  diff: number
  route: string
}

export type HomeDashboardData = {
  warnings: string[]
  asOf: string
  monthKey: string
  monthLabel: string
  portfolio: {
    buildingCount: number; totalUnits: number; occupiedUnits: number; vacantUnits: number
    occupancyPercent: number; futureOccupiedUnits: number; futureOccupancyPercent: number
  }
  leases: { expiring30Count: number; upcomingMoveInCount: number; upcomingMoveOutCount: number; events: HomeLeaseEvent[] }
  receivables: {
    totalOpenItemsNet: number; totalDiff: number; rowCount: number; mismatchRowCount: number
    mismatches: HomeMismatchItem[]; currentMonthBilled: number; currentMonthCollected: number
  }
  maintenance: {
    openItemCount: number; overdueCount: number; items: HomeMaintenanceItem[]
    agingBuckets: { label: string; value: number }[]
  }
  periods: { pendingCloseCount: number; lastClosedPeriod: string | null; nextClosablePeriod: string | null; firstGapPeriod: string | null }
  charts: { collections: HomeLineChartData; occupancy: HomeLineChartData; maintenanceAging: HomeBarChartData }
}

type DashboardResponse = {
  asOfUtc: string
  warnings: string[]
  portfolio: HomeDashboardData['portfolio']
  leases: {
    expiring30Count: number; upcomingMoveInCount: number; upcomingMoveOutCount: number
    events: { kind: 'Move-in' | 'Move-out'; date: string; leaseId: string; leaseDisplay: string; propertyDisplay: string }[]
  }
  receivables: {
    totalOpenItemsNet: number; totalDiff: number; rowCount: number; mismatchRowCount: number
    currentMonthBilled: number; currentMonthCollected: number
    mismatches: {
      partyId: string; propertyId: string; leaseId: string; leaseDisplay: string
      propertyDisplay: string; rowKind: string; diff: number
    }[]
  }
  maintenance: {
    openItemCount: number; overdueCount: number
    items: {
      requestId: string; workOrderId?: string | null; queueState: string; subject: string
      requestDisplay: string; propertyDisplay: string; requestedAtUtc: string; dueByUtc?: string | null
      agingDays: number; assignedTo?: string | null
    }[]
    aging: { days0To3: number; days4To7: number; days8To14: number; days15Plus: number }
  }
  periods: { pendingCloseCount: number; lastClosedPeriod?: string | null; nextClosablePeriod?: string | null; firstGapPeriod?: string | null }
  occupancyTrend: { month: string; occupiedUnits: number; vacantUnits: number }[]
  collectionsTrend: { month: string; billed: number; collected: number }[]
}

function openItemsRoute(item: DashboardResponse['receivables']['mismatches'][number]): string {
  const params = new URLSearchParams({ leaseId: item.leaseId })
  if (isGuidString(item.partyId)) params.set('partyId', item.partyId)
  if (isGuidString(item.propertyId)) params.set('propertyId', item.propertyId)
  return `${buildPmOpenItemsPath('receivables')}?${params.toString()}`
}

function shortMonth(value: string): string {
  const date = parseDashboardUtcDateOnly(value)
  return date?.toLocaleString(undefined, { month: 'short', timeZone: 'UTC' }) ?? value
}

function maintenanceState(value: string): string {
  return value === 'WorkOrdered' ? 'Work ordered' : value
}

export async function loadHomeDashboard(asOf: string, signal?: AbortSignal): Promise<HomeDashboardData> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Select a valid as-of date.')

  const response = signal
    ? await httpGet<DashboardResponse>('/api/dashboard', { asOfUtc: asOf }, { signal })
    : await httpGet<DashboardResponse>('/api/dashboard', { asOfUtc: asOf })
  const monthKey = toDashboardUtcMonthKey(asOfDate)
  const fallbackWindow = buildDashboardMonthWindow(asOfDate, 12)
  const occupancy = response.occupancyTrend ?? []
  const collections = response.collectionsTrend ?? []
  const occupancyLabels = occupancy.length > 0 ? occupancy.map((item) => shortMonth(item.month)) : fallbackWindow.labels
  const collectionLabels = collections.length > 0 ? collections.map((item) => shortMonth(item.month)) : fallbackWindow.labels
  const zeroes = () => Array.from({ length: 12 }, () => 0)

  const maintenanceReportUrl = buildReportPageUrl('pm.maintenance.queue', {
    context: {
      reportCode: 'pm.maintenance.queue',
      request: { parameters: { as_of_utc: asOf }, filters: { queue_state: { value: 'Overdue' } }, layout: null, offset: 0, limit: 500, cursor: null },
    },
  })

  return {
    warnings: response.warnings ?? [],
    asOf,
    monthKey,
    monthLabel: formatDashboardMonthLabel(monthKey),
    portfolio: response.portfolio,
    leases: {
      ...response.leases,
      events: (response.leases.events ?? []).map((item) => ({
        kind: item.kind,
        date: item.date,
        leaseDisplay: item.leaseDisplay,
        propertyDisplay: item.propertyDisplay,
        route: buildDocumentFullPageUrl('pm.lease', item.leaseId),
      })),
    },
    receivables: {
      ...response.receivables,
      mismatches: (response.receivables.mismatches ?? []).map((item) => ({
        leaseDisplay: item.leaseDisplay,
        propertyDisplay: item.propertyDisplay,
        rowKind: item.rowKind,
        diff: item.diff,
        route: openItemsRoute(item),
      })),
    },
    maintenance: {
      openItemCount: response.maintenance.openItemCount,
      overdueCount: response.maintenance.overdueCount,
      items: (response.maintenance.items ?? []).map((item) => ({
        queueState: maintenanceState(item.queueState),
        subject: item.subject,
        requestDisplay: item.requestDisplay,
        propertyDisplay: item.propertyDisplay,
        requestedAt: item.requestedAtUtc,
        dueBy: item.dueByUtc ?? null,
        agingDays: item.agingDays,
        assignedTo: item.assignedTo ?? null,
        route: buildDocumentFullPageUrl(
          item.workOrderId ? 'pm.work_order' : 'pm.maintenance_request',
          item.workOrderId ?? item.requestId,
        ),
      })),
      agingBuckets: [
        { label: '0-3 days', value: response.maintenance.aging.days0To3 },
        { label: '4-7 days', value: response.maintenance.aging.days4To7 },
        { label: '8-14 days', value: response.maintenance.aging.days8To14 },
        { label: '15+ days', value: response.maintenance.aging.days15Plus },
      ],
    },
    periods: {
      pendingCloseCount: response.periods.pendingCloseCount,
      lastClosedPeriod: formatDashboardMonthChip(response.periods.lastClosedPeriod),
      nextClosablePeriod: formatDashboardMonthChip(response.periods.nextClosablePeriod),
      firstGapPeriod: formatDashboardMonthChip(response.periods.firstGapPeriod),
    },
    charts: {
      collections: {
        title: 'Collections trend', subtitle: 'Billed vs collected across the last 12 months', labels: collectionLabels,
        series: [
          { label: 'Billed', color: 'var(--ngb-blue)', values: collections.length > 0 ? collections.map((item) => item.billed) : zeroes() },
          { label: 'Collected', color: 'var(--ngb-accent-1)', values: collections.length > 0 ? collections.map((item) => item.collected) : zeroes() },
        ],
        route: buildPmReconciliationPath('receivables', { fromMonth: fallbackWindow.monthKeys[0]!, toMonth: monthKey, mode: 'Movement' }),
      },
      occupancy: {
        title: 'Occupancy trend', subtitle: 'Occupied and vacant units over the last 12 months', labels: occupancyLabels,
        series: [
          { label: 'Occupied', color: 'var(--ngb-accent-1)', values: occupancy.length > 0 ? occupancy.map((item) => item.occupiedUnits) : zeroes() },
          { label: 'Vacant', color: 'var(--ngb-warn)', values: occupancy.length > 0 ? occupancy.map((item) => item.vacantUnits) : zeroes() },
        ],
        route: buildReportPageUrl('pm.occupancy.summary', { context: { reportCode: 'pm.occupancy.summary', request: { parameters: { as_of_utc: asOf }, filters: null, layout: null, offset: 0, limit: 500, cursor: null } } }),
      },
      maintenanceAging: {
        title: 'Maintenance aging', subtitle: 'Open maintenance backlog by aging bucket',
        labels: ['0-3 days', '4-7 days', '8-14 days', '15+ days'],
        series: [{ label: 'Open items', color: 'var(--ngb-warn)', values: [
          response.maintenance.aging.days0To3, response.maintenance.aging.days4To7,
          response.maintenance.aging.days8To14, response.maintenance.aging.days15Plus,
        ] }],
        route: maintenanceReportUrl,
      },
    },
  }
}
