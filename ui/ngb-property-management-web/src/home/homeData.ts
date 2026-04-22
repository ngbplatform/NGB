import { getReceivablesReconciliation } from '../api/clients/receivables'
import { buildPmOpenItemsPath, buildPmReconciliationPath } from '../router/pmRoutePaths'
import {
  addDashboardUtcDays,
  buildDashboardMonthWindow,
  buildDocumentFullPageUrl,
  buildReportPageUrl,
  captureDashboardValue,
  compareDashboardUtcDateOnly,
  dashboardFieldDateOnly,
  dashboardFieldDisplay,
  dashboardFieldMoney,
  dashboardReportCellByCode,
  dashboardReportCellDisplay,
  dashboardReportCellNumber,
  dashboardReportColumnIndexMap,
  executeReport,
  fetchAllPagedDashboardDocuments,
  formatDashboardMonthChip,
  formatDashboardMonthLabel,
  getDocumentPage,
  isDashboardReportRowKind,
  isDashboardUtcDateWithinRange,
  isPostedDashboardDocument,
  loadDashboardPeriodClosingSummary,
  parseDashboardUtcDateOnly,
  ReportRowKind,
  resolveReportCellActionUrl,
  startOfDashboardUtcMonth,
  type DocumentDto,
  toDashboardInteger,
  toDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
  isGuidString,
} from 'ngb-ui-framework'

export type HomeTrendSeries = {
  label: string
  color: string
  values: number[]
}

export type HomeLineChartData = {
  title: string
  subtitle: string
  labels: string[]
  series: HomeTrendSeries[]
  route: string
}

export type HomeBarChartData = {
  title: string
  subtitle: string
  labels: string[]
  series: HomeTrendSeries[]
  route: string
}

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
    buildingCount: number
    totalUnits: number
    occupiedUnits: number
    vacantUnits: number
    occupancyPercent: number
    futureOccupiedUnits: number
    futureOccupancyPercent: number
  }
  leases: {
    expiring30Count: number
    upcomingMoveInCount: number
    upcomingMoveOutCount: number
    events: HomeLeaseEvent[]
  }
  receivables: {
    totalOpenItemsNet: number
    totalDiff: number
    rowCount: number
    mismatchRowCount: number
    mismatches: HomeMismatchItem[]
    currentMonthBilled: number
    currentMonthCollected: number
  }
  maintenance: {
    openItemCount: number
    overdueCount: number
    items: HomeMaintenanceItem[]
    agingBuckets: { label: string; value: number }[]
  }
  periods: {
    pendingCloseCount: number
    lastClosedPeriod: string | null
    nextClosablePeriod: string | null
    firstGapPeriod: string | null
  }
  charts: {
    collections: HomeLineChartData
    occupancy: HomeLineChartData
    maintenanceAging: HomeBarChartData
  }
}

type OccupancySnapshot = {
  buildingCount: number
  totalUnits: number
  occupiedUnits: number
  vacantUnits: number
  occupancyPercent: number
}

type LeaseAnalytics = {
  futureOccupiedUnits: number
  futureOccupancyPercent: number
  expiring30Count: number
  upcomingMoveInCount: number
  upcomingMoveOutCount: number
  events: HomeLeaseEvent[]
}

type OccupancyTrend = {
  labels: string[]
  occupiedTrend: number[]
  vacantTrend: number[]
}

type CollectionsAnalytics = {
  labels: string[]
  billed: number[]
  collected: number[]
  currentMonthBilled: number
  currentMonthCollected: number
}

function buildReceivablesOpenItemsUrl(args: {
  leaseId: string
  partyId?: string | null
  propertyId?: string | null
}): string {
  const params = new URLSearchParams()
  params.set('leaseId', args.leaseId)
  if (isGuidString(args.partyId)) params.set('partyId', args.partyId)
  if (isGuidString(args.propertyId)) params.set('propertyId', args.propertyId)
  return `${buildPmOpenItemsPath('receivables')}?${params.toString()}`
}

async function loadOccupancySnapshot(asOf: string): Promise<OccupancySnapshot> {
  const response = await executeReport('pm.occupancy.summary', {
    parameters: { as_of_utc: asOf },
    offset: 0,
    limit: 500,
  })

  const columns = dashboardReportColumnIndexMap(response)
  const detailRows = (response.sheet.rows ?? []).filter((row) => isDashboardReportRowKind(row, ReportRowKind.Detail))
  const totalRow = (response.sheet.rows ?? []).find((row) => isDashboardReportRowKind(row, ReportRowKind.Total))

  const totalUnits = totalRow
    ? dashboardReportCellNumber(totalRow, columns, 'total_units')
    : detailRows.reduce((sum, row) => sum + dashboardReportCellNumber(row, columns, 'total_units'), 0)
  const occupiedUnits = totalRow
    ? dashboardReportCellNumber(totalRow, columns, 'occupied_units')
    : detailRows.reduce((sum, row) => sum + dashboardReportCellNumber(row, columns, 'occupied_units'), 0)
  const vacantUnits = totalRow
    ? dashboardReportCellNumber(totalRow, columns, 'vacant_units')
    : detailRows.reduce((sum, row) => sum + dashboardReportCellNumber(row, columns, 'vacant_units'), 0)
  const occupancyPercent = totalRow
    ? dashboardReportCellNumber(totalRow, columns, 'occupancy_percent')
    : (totalUnits > 0 ? (occupiedUnits / totalUnits) * 100 : 0)

  return {
    buildingCount: typeof response.total === 'number' ? response.total : detailRows.length,
    totalUnits,
    occupiedUnits,
    vacantUnits,
    occupancyPercent,
  }
}

async function loadLeaseAnalytics(asOf: string, totalUnits: number): Promise<LeaseAnalytics> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Invalid as-of date.')

  const futureDate = addDashboardUtcDays(asOfDate, 30)
  const snapshotEnd = addDashboardUtcDays(asOfDate, 14)
  const [leaseDocuments, futureOccupancy] = await Promise.all([
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.lease'),
    loadOccupancySnapshot(toDashboardUtcDateOnly(futureDate)),
  ])
  const postedLeaseDocuments = leaseDocuments.filter(isPostedDashboardDocument)
  const denominator = futureOccupancy.totalUnits > 0 ? futureOccupancy.totalUnits : totalUnits
  const expiring = postedLeaseDocuments.filter((document) =>
    isDashboardUtcDateWithinRange(dashboardFieldDateOnly(document, 'end_on_utc'), asOfDate, futureDate))
  const moveIns = postedLeaseDocuments.filter((document) =>
    isDashboardUtcDateWithinRange(dashboardFieldDateOnly(document, 'start_on_utc'), asOfDate, snapshotEnd))
  const moveOuts = postedLeaseDocuments.filter((document) =>
    isDashboardUtcDateWithinRange(dashboardFieldDateOnly(document, 'end_on_utc'), asOfDate, snapshotEnd))

  const leaseEvents: HomeLeaseEvent[] = [
    ...moveIns.map((document) => ({
      kind: 'Move-in' as const,
      date: dashboardFieldDateOnly(document, 'start_on_utc') ?? '',
      leaseDisplay: String(document.display ?? document.id).trim(),
      propertyDisplay: dashboardFieldDisplay(document, 'property_id') ?? 'Property',
      route: buildDocumentFullPageUrl('pm.lease', document.id),
    })),
    ...moveOuts.map((document) => ({
      kind: 'Move-out' as const,
      date: dashboardFieldDateOnly(document, 'end_on_utc') ?? '',
      leaseDisplay: String(document.display ?? document.id).trim(),
      propertyDisplay: dashboardFieldDisplay(document, 'property_id') ?? 'Property',
      route: buildDocumentFullPageUrl('pm.lease', document.id),
    })),
  ]
    .sort((left, right) => compareDashboardUtcDateOnly(left.date, right.date))
    .slice(0, 6)

  return {
    futureOccupiedUnits: futureOccupancy.occupiedUnits,
    futureOccupancyPercent: denominator > 0 ? (futureOccupancy.occupiedUnits / denominator) * 100 : 0,
    expiring30Count: expiring.length,
    upcomingMoveInCount: moveIns.length,
    upcomingMoveOutCount: moveOuts.length,
    events: leaseEvents,
  }
}

async function loadOccupancyTrend(
  asOfDate: Date,
  currentSnapshot?: OccupancySnapshot | null,
): Promise<OccupancyTrend> {
  const monthWindow = buildDashboardMonthWindow(asOfDate, 12)
  const currentMonthKey = toDashboardUtcMonthKey(asOfDate)
  const snapshots = await Promise.all(
    monthWindow.pointDates.map(async (pointDate, index) => {
      const monthKey = monthWindow.monthKeys[index] ?? ''
      if (currentSnapshot && monthKey === currentMonthKey) return currentSnapshot
      return await loadOccupancySnapshot(toDashboardUtcDateOnly(pointDate))
    }),
  )

  return {
    labels: monthWindow.labels,
    occupiedTrend: snapshots.map((snapshot) => snapshot.occupiedUnits),
    vacantTrend: snapshots.map((snapshot) => snapshot.vacantUnits),
  }
}

function normalizeSeriesValues(labels: string[]): Record<string, number> {
  return Object.fromEntries(labels.map((label) => [label, 0]))
}

function accumulateMonthTotals(
  target: Record<string, number>,
  documents: DocumentDto[],
  fieldKey: string,
  amountSign = 1,
): void {
  for (const document of documents) {
    if (!isPostedDashboardDocument(document)) continue

    const dateOnly = dashboardFieldDateOnly(document, fieldKey)
    const parsed = parseDashboardUtcDateOnly(dateOnly)
    if (!parsed) continue

    const monthKey = toDashboardUtcMonthKey(parsed)
    if (!(monthKey in target)) continue
    target[monthKey] += dashboardFieldMoney(document, 'amount') * amountSign
  }
}

async function loadCollectionsAnalytics(asOf: string): Promise<CollectionsAnalytics> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Invalid as-of date.')

  const monthWindow = buildDashboardMonthWindow(asOfDate, 12)
  const fromInclusive = `${monthWindow.monthKeys[0] ?? toDashboardUtcMonthKey(asOfDate)}-01`
  const toInclusive = toDashboardUtcDateOnly(asOfDate)
  const monthKeys = monthWindow.monthKeys

  const [rentCharges, receivableCharges, lateFeeCharges, receivablePayments, returnedPayments] = await Promise.all([
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.rent_charge', { periodFrom: fromInclusive, periodTo: toInclusive }),
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.receivable_charge', { periodFrom: fromInclusive, periodTo: toInclusive }),
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.late_fee_charge', { periodFrom: fromInclusive, periodTo: toInclusive }),
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.receivable_payment', { periodFrom: fromInclusive, periodTo: toInclusive }),
    fetchAllPagedDashboardDocuments(getDocumentPage, 'pm.receivable_returned_payment', { periodFrom: fromInclusive, periodTo: toInclusive }),
  ])

  const billedByMonth = normalizeSeriesValues(monthKeys)
  const collectedByMonth = normalizeSeriesValues(monthKeys)

  accumulateMonthTotals(billedByMonth, [...rentCharges, ...receivableCharges, ...lateFeeCharges], 'due_on_utc')
  accumulateMonthTotals(collectedByMonth, receivablePayments, 'received_on_utc')
  accumulateMonthTotals(collectedByMonth, returnedPayments, 'returned_on_utc', -1)

  const billed = monthKeys.map((monthKey) => billedByMonth[monthKey] ?? 0)
  const collected = monthKeys.map((monthKey) => collectedByMonth[monthKey] ?? 0)

  return {
    labels: monthWindow.labels,
    billed,
    collected,
    currentMonthBilled: billed[billed.length - 1] ?? 0,
    currentMonthCollected: collected[collected.length - 1] ?? 0,
  }
}

async function loadMaintenanceSnapshot(asOf: string): Promise<{
  openItemCount: number
  overdueCount: number
  items: HomeMaintenanceItem[]
  agingBuckets: { label: string; value: number }[]
}> {
  const [fullResponse, overdueResponse] = await Promise.all([
    executeReport('pm.maintenance.queue', {
      parameters: { as_of_utc: asOf },
      offset: 0,
      limit: 500,
    }),
    executeReport('pm.maintenance.queue', {
      parameters: { as_of_utc: asOf },
      filters: {
        queue_state: { value: 'Overdue' },
      },
      offset: 0,
      limit: 1,
    }),
  ])

  const columns = dashboardReportColumnIndexMap(fullResponse)
  const rows = (fullResponse.sheet.rows ?? [])
    .filter((row) => isDashboardReportRowKind(row, ReportRowKind.Detail))
    .map((row) => {
      const workOrderAction = dashboardReportCellByCode(row, columns, 'work_order')?.action ?? null
      const requestAction = dashboardReportCellByCode(row, columns, 'request')?.action ?? null

      return {
        queueState: dashboardReportCellDisplay(row, columns, 'queue_state') || 'Requested',
        subject: dashboardReportCellDisplay(row, columns, 'subject') || 'Maintenance item',
        requestDisplay: dashboardReportCellDisplay(row, columns, 'request') || 'Request',
        propertyDisplay: dashboardReportCellDisplay(row, columns, 'property')
          || dashboardReportCellDisplay(row, columns, 'building')
          || 'Property',
        requestedAt: dashboardReportCellDisplay(row, columns, 'requested_at_utc') || null,
        dueBy: dashboardReportCellDisplay(row, columns, 'due_by_utc') || null,
        agingDays: toDashboardInteger(
          dashboardReportCellByCode(row, columns, 'aging_days')?.value
          ?? dashboardReportCellDisplay(row, columns, 'aging_days'),
        ),
        assignedTo: dashboardReportCellDisplay(row, columns, 'assigned_to') || null,
        route: resolveReportCellActionUrl(workOrderAction ?? requestAction),
      }
    })

  const stateWeight: Record<string, number> = {
    Overdue: 0,
    WorkOrdered: 1,
    Requested: 2,
  }

  rows.sort((left, right) => {
    const leftWeight = stateWeight[left.queueState] ?? 10
    const rightWeight = stateWeight[right.queueState] ?? 10
    if (leftWeight !== rightWeight) return leftWeight - rightWeight
    return right.agingDays - left.agingDays
  })

  const agingBuckets = [
    { label: '0-3 days', value: 0 },
    { label: '4-7 days', value: 0 },
    { label: '8-14 days', value: 0 },
    { label: '15+ days', value: 0 },
  ]

  for (const row of rows) {
    if (row.agingDays >= 15) agingBuckets[3]!.value += 1
    else if (row.agingDays >= 8) agingBuckets[2]!.value += 1
    else if (row.agingDays >= 4) agingBuckets[1]!.value += 1
    else agingBuckets[0]!.value += 1
  }

  return {
    openItemCount: typeof fullResponse.total === 'number' ? fullResponse.total : rows.length,
    overdueCount: typeof overdueResponse.total === 'number'
      ? overdueResponse.total
      : (overdueResponse.sheet.rows ?? []).filter((row) => isDashboardReportRowKind(row, ReportRowKind.Detail)).length,
    items: rows.slice(0, 6),
    agingBuckets,
  }
}

async function loadReceivablesSnapshot(asOf: string): Promise<{
  totalOpenItemsNet: number
  totalDiff: number
  rowCount: number
  mismatchRowCount: number
  mismatches: HomeMismatchItem[]
}> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Invalid as-of date.')

  const monthStart = toDashboardUtcDateOnly(startOfDashboardUtcMonth(asOfDate))
  const report = await getReceivablesReconciliation({
    fromMonthInclusive: monthStart,
    toMonthInclusive: monthStart,
    mode: 'Balance',
  })

  const mismatches = (report.rows ?? [])
    .filter((row) => row.rowKind !== 'Matched')
    .slice()
    .sort((left, right) => Math.abs(right.diff) - Math.abs(left.diff))
    .slice(0, 6)
    .map((row) => ({
      leaseDisplay: String(row.leaseDisplay ?? row.leaseId ?? 'Lease').trim(),
      propertyDisplay: String(row.propertyDisplay ?? row.propertyId ?? 'Property').trim(),
      rowKind: row.rowKind,
      diff: row.diff,
      route: buildReceivablesOpenItemsUrl({
        leaseId: row.leaseId,
        partyId: row.partyId,
        propertyId: row.propertyId,
      }),
    }))

  return {
    totalOpenItemsNet: report.totalOpenItemsNet,
    totalDiff: report.totalDiff,
    rowCount: report.rowCount,
    mismatchRowCount: report.mismatchRowCount,
    mismatches,
  }
}

async function loadPeriodsSnapshot(asOf: string) {
  return await loadDashboardPeriodClosingSummary(asOf)
}

export async function loadHomeDashboard(asOf: string): Promise<HomeDashboardData> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Select a valid as-of date.')

  const monthKey = toDashboardUtcMonthKey(asOfDate)
  const monthLabel = formatDashboardMonthLabel(monthKey)
  const warnings: string[] = []

  const occupancyResult = await captureDashboardValue('Occupancy summary is unavailable', () => loadOccupancySnapshot(asOf))
  if (occupancyResult.warning) warnings.push(occupancyResult.warning)

  const leaseResult = await captureDashboardValue('Lease analytics are unavailable', () =>
    loadLeaseAnalytics(asOf, occupancyResult.value?.totalUnits ?? 0),
  )
  if (leaseResult.warning) warnings.push(leaseResult.warning)

  const [occupancyTrendResult, maintenanceResult, receivablesResult, periodsResult, collectionsResult] = await Promise.all([
    captureDashboardValue('Occupancy trend is unavailable', () => loadOccupancyTrend(asOfDate, occupancyResult.value)),
    captureDashboardValue('Maintenance queue is unavailable', () => loadMaintenanceSnapshot(asOf)),
    captureDashboardValue('Receivables reconciliation is unavailable', () => loadReceivablesSnapshot(asOf)),
    captureDashboardValue('Period closing status is unavailable', () => loadPeriodsSnapshot(asOf)),
    captureDashboardValue('Collections trend is unavailable', () => loadCollectionsAnalytics(asOf)),
  ])

  if (occupancyTrendResult.warning) warnings.push(occupancyTrendResult.warning)
  if (maintenanceResult.warning) warnings.push(maintenanceResult.warning)
  if (receivablesResult.warning) warnings.push(receivablesResult.warning)
  if (periodsResult.warning) warnings.push(periodsResult.warning)
  if (collectionsResult.warning) warnings.push(collectionsResult.warning)

  const monthWindow = buildDashboardMonthWindow(asOfDate, 12)
  const occupancy = occupancyResult.value ?? {
    buildingCount: 0,
    totalUnits: 0,
    occupiedUnits: 0,
    vacantUnits: 0,
    occupancyPercent: 0,
  }
  const leaseAnalytics = leaseResult.value ?? {
    futureOccupiedUnits: 0,
    futureOccupancyPercent: 0,
    expiring30Count: 0,
    upcomingMoveInCount: 0,
    upcomingMoveOutCount: 0,
    events: [],
  }
  const occupancyTrend = occupancyTrendResult.value ?? {
    labels: monthWindow.labels,
    occupiedTrend: Array.from({ length: 12 }, () => 0),
    vacantTrend: Array.from({ length: 12 }, () => 0),
  }
  const maintenance = maintenanceResult.value ?? {
    openItemCount: 0,
    overdueCount: 0,
    items: [],
    agingBuckets: [
      { label: '0-3 days', value: 0 },
      { label: '4-7 days', value: 0 },
      { label: '8-14 days', value: 0 },
      { label: '15+ days', value: 0 },
    ],
  }
  const receivables = receivablesResult.value ?? {
    totalOpenItemsNet: 0,
    totalDiff: 0,
    rowCount: 0,
    mismatchRowCount: 0,
    mismatches: [],
  }
  const periods = periodsResult.value ?? {
    pendingCloseCount: 0,
    lastClosedPeriod: null,
    nextClosablePeriod: null,
    firstGapPeriod: null,
  }
  const collections = collectionsResult.value ?? {
    labels: monthWindow.labels,
    billed: Array.from({ length: 12 }, () => 0),
    collected: Array.from({ length: 12 }, () => 0),
    currentMonthBilled: 0,
    currentMonthCollected: 0,
  }

  const maintenanceReportUrl = buildReportPageUrl('pm.maintenance.queue', {
    context: {
      reportCode: 'pm.maintenance.queue',
      request: {
        parameters: { as_of_utc: asOf },
        filters: { queue_state: { value: 'Overdue' } },
        layout: null,
        offset: 0,
        limit: 500,
        cursor: null,
      },
    },
  })

  return {
    warnings,
    asOf,
    monthKey,
    monthLabel,
    portfolio: {
      buildingCount: occupancy.buildingCount,
      totalUnits: occupancy.totalUnits,
      occupiedUnits: occupancy.occupiedUnits,
      vacantUnits: occupancy.vacantUnits,
      occupancyPercent: occupancy.occupancyPercent,
      futureOccupiedUnits: leaseAnalytics.futureOccupiedUnits,
      futureOccupancyPercent: leaseAnalytics.futureOccupancyPercent,
    },
    leases: {
      expiring30Count: leaseAnalytics.expiring30Count,
      upcomingMoveInCount: leaseAnalytics.upcomingMoveInCount,
      upcomingMoveOutCount: leaseAnalytics.upcomingMoveOutCount,
      events: leaseAnalytics.events,
    },
    receivables: {
      totalOpenItemsNet: receivables.totalOpenItemsNet,
      totalDiff: receivables.totalDiff,
      rowCount: receivables.rowCount,
      mismatchRowCount: receivables.mismatchRowCount,
      mismatches: receivables.mismatches,
      currentMonthBilled: collections.currentMonthBilled,
      currentMonthCollected: collections.currentMonthCollected,
    },
    maintenance: {
      ...maintenance,
      items: maintenance.items,
    },
    periods: {
      ...periods,
      lastClosedPeriod: formatDashboardMonthChip(periods.lastClosedPeriod),
      nextClosablePeriod: formatDashboardMonthChip(periods.nextClosablePeriod),
      firstGapPeriod: formatDashboardMonthChip(periods.firstGapPeriod),
    },
    charts: {
      collections: {
        title: 'Collections trend',
        subtitle: 'Billed vs collected across the last 12 months',
        labels: collections.labels,
        series: [
          { label: 'Billed', color: 'var(--ngb-blue)', values: collections.billed },
          { label: 'Collected', color: 'var(--ngb-accent-1)', values: collections.collected },
        ],
        route: buildPmReconciliationPath('receivables', {
          fromMonth: monthWindow.monthKeys[0] ?? monthKey,
          toMonth: monthKey,
          mode: 'Movement',
        }),
      },
      occupancy: {
        title: 'Occupancy trend',
        subtitle: 'Occupied and vacant units over the last 12 months',
        labels: occupancyTrend.labels,
        series: [
          { label: 'Occupied', color: 'var(--ngb-accent-1)', values: occupancyTrend.occupiedTrend },
          { label: 'Vacant', color: 'var(--ngb-warn)', values: occupancyTrend.vacantTrend },
        ],
        route: buildReportPageUrl('pm.occupancy.summary', {
          context: {
            reportCode: 'pm.occupancy.summary',
            request: {
              parameters: { as_of_utc: asOf },
              filters: null,
              layout: null,
              offset: 0,
              limit: 500,
              cursor: null,
            },
          },
        }),
      },
      maintenanceAging: {
        title: 'Maintenance aging',
        subtitle: 'Open maintenance backlog by aging bucket',
        labels: maintenance.agingBuckets.map((bucket) => bucket.label),
        series: [
          { label: 'Open items', color: 'var(--ngb-warn)', values: maintenance.agingBuckets.map((bucket) => bucket.value) },
        ],
        route: maintenanceReportUrl,
      },
    },
  }
}
