import {
  buildReportPageUrl,
  dashboardReportCellByCode,
  dashboardReportCellDisplay,
  dashboardReportCellNumber,
  dashboardReportColumnIndexMap,
  executeReport,
  formatDashboardMonthLabel,
  isDashboardReportRowKind,
  parseDashboardUtcDateOnly,
  ReportRowKind,
  resolveReportCellActionUrl,
  startOfDashboardUtcMonth,
  toDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
  type ReportExecutionRequestDto,
  type ReportExecutionResponseDto,
  type ReportSheetRowDto,
} from '@ngbplatform/ui'

export type TradeHomeTrendSeries = {
  label: string
  color: string
  values: number[]
}

export type TradeHomeBarChartData = {
  title: string
  subtitle: string
  labels: string[]
  series: TradeHomeTrendSeries[]
  route: string
}

export type TradeHomeTopItem = {
  item: string
  soldQuantity: number
  netSales: number
  grossMargin: number
  marginPercent: number
  route: string | null
}

export type TradeHomeTopCustomer = {
  customer: string
  salesDocumentCount: number
  returnDocumentCount: number
  netSales: number
  grossMargin: number
  marginPercent: number
  route: string | null
}

export type TradeHomeTopVendor = {
  vendor: string
  purchaseDocumentCount: number
  returnDocumentCount: number
  netPurchases: number
  route: string | null
}

export type TradeHomeInventoryPosition = {
  item: string
  warehouse: string
  quantity: number
  route: string | null
  itemRoute: string | null
  warehouseRoute: string | null
}

export type TradeHomeRecentDocument = {
  title: string
  amountDisplay: string | null
  documentDate: string | null
  notes: string
  route: string | null
}

export type TradeHomeRoutes = {
  sales: string
  purchases: string
  inventory: string
  grossMargin: string
  currentPrices: string
  salesByItem: string
  salesByCustomer: string
  purchasesByVendor: string
}

export type TradeHomeDashboardData = {
  warnings: string[]
  asOf: string
  monthKey: string
  monthLabel: string
  salesThisMonth: number
  purchasesThisMonth: number
  inventoryOnHand: number
  grossMargin: number
  activeSalesItemCount: number
  activeCustomerCount: number
  activeVendorCount: number
  inventoryPositionCount: number
  topItems: TradeHomeTopItem[]
  topCustomers: TradeHomeTopCustomer[]
  topVendors: TradeHomeTopVendor[]
  inventoryPositions: TradeHomeInventoryPosition[]
  recentDocuments: TradeHomeRecentDocument[]
  charts: {
    salesMix: TradeHomeBarChartData
    inventoryFootprint: TradeHomeBarChartData
  }
  routes: TradeHomeRoutes
}

type OverviewSnapshot = {
  salesThisMonth: number
  purchasesThisMonth: number
  inventoryOnHand: number
  grossMargin: number
  inventoryPositionCount: number
  activeSalesItemCount: number
  activeCustomerCount: number
  activeVendorCount: number
  topItems: TradeHomeTopItem[]
  topCustomers: TradeHomeTopCustomer[]
  topVendors: TradeHomeTopVendor[]
  inventoryPositions: TradeHomeInventoryPosition[]
  recentDocuments: TradeHomeRecentDocument[]
  routes: Pick<TradeHomeRoutes, 'sales' | 'purchases' | 'inventory' | 'grossMargin'>
}

const REPORTS = {
  dashboardOverview: 'trd.dashboard_overview',
  salesByItem: 'trd.sales_by_item',
  salesByCustomer: 'trd.sales_by_customer',
  purchasesByVendor: 'trd.purchases_by_vendor',
  inventoryBalances: 'trd.inventory_balances',
  currentItemPrices: 'trd.current_item_prices',
} as const

function buildReportUrl(
  reportCode: string,
  request?: ReportExecutionRequestDto,
): string {
  return buildReportPageUrl(reportCode, {
    context: {
      reportCode,
      request: {
        layout: request?.layout ?? null,
        filters: request?.filters ?? null,
        parameters: request?.parameters ?? null,
        variantCode: request?.variantCode ?? null,
        offset: request?.offset ?? 0,
        limit: request?.limit ?? 500,
        cursor: request?.cursor ?? null,
      },
    },
  })
}

function buildDefaultRoutes(
  fromInclusive: string,
  toInclusive: string,
  asOf: string,
): TradeHomeRoutes {
  return {
    sales: buildReportUrl(REPORTS.salesByCustomer, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
    purchases: buildReportUrl(REPORTS.purchasesByVendor, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
    inventory: buildReportUrl(REPORTS.inventoryBalances, {
      parameters: {
        as_of_utc: asOf,
      },
    }),
    grossMargin: buildReportUrl(REPORTS.salesByItem, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
    currentPrices: buildReportUrl(REPORTS.currentItemPrices),
    salesByItem: buildReportUrl(REPORTS.salesByItem, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
    salesByCustomer: buildReportUrl(REPORTS.salesByCustomer, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
    purchasesByVendor: buildReportUrl(REPORTS.purchasesByVendor, {
      parameters: {
        from_utc: fromInclusive,
        to_utc: toInclusive,
      },
    }),
  }
}

function reportDetailRows(response: ReportExecutionResponseDto): ReportSheetRowDto[] {
  return (response.sheet.rows ?? []).filter((row) => isDashboardReportRowKind(row, ReportRowKind.Detail))
}

function buildAsOfRequest(asOf: string): ReportExecutionRequestDto {
  return {
    parameters: {
      as_of_utc: asOf,
    },
    layout: null,
    filters: null,
    variantCode: null,
    offset: 0,
    limit: 500,
    cursor: null,
  }
}

async function loadOverviewSnapshot(
  asOf: string,
  defaultRoutes: TradeHomeRoutes,
  signal?: AbortSignal,
): Promise<OverviewSnapshot> {
  const response = signal
    ? await executeReport(REPORTS.dashboardOverview, buildAsOfRequest(asOf), { signal })
    : await executeReport(REPORTS.dashboardOverview, buildAsOfRequest(asOf))
  const columns = dashboardReportColumnIndexMap(response)
  const rows = reportDetailRows(response)

  let salesThisMonth = 0
  let purchasesThisMonth = 0
  let inventoryOnHand = 0
  let grossMargin = 0

  let salesRoute = defaultRoutes.sales
  let purchasesRoute = defaultRoutes.purchases
  let inventoryRoute = defaultRoutes.inventory
  let grossMarginRoute = defaultRoutes.grossMargin

  const inventoryPositions: TradeHomeInventoryPosition[] = []
  const recentDocuments: TradeHomeRecentDocument[] = []
  const topItems: TradeHomeTopItem[] = []
  const topCustomers: TradeHomeTopCustomer[] = []
  const topVendors: TradeHomeTopVendor[] = []
  const rawInventoryPositionCount = Number(response.diagnostics?.inventory_position_count ?? 0)
  const inventoryPositionCount = Number.isFinite(rawInventoryPositionCount) && rawInventoryPositionCount >= 0
    ? rawInventoryPositionCount
    : 0

  for (const row of rows) {
    const category = dashboardReportCellDisplay(row, columns, 'category')
    const subject = dashboardReportCellDisplay(row, columns, 'subject')
    const valueCell = dashboardReportCellByCode(row, columns, 'value')
    const subjectCell = dashboardReportCellByCode(row, columns, 'subject')
    const secondaryCell = dashboardReportCellByCode(row, columns, 'secondary')

    if (category === 'KPI') {
      const route = resolveReportCellActionUrl(valueCell?.action ?? null)
      switch (subject.toLowerCase()) {
        case 'sales this month':
          salesThisMonth = dashboardReportCellNumber(row, columns, 'value')
          salesRoute = route ?? salesRoute
          break
        case 'purchases this month':
          purchasesThisMonth = dashboardReportCellNumber(row, columns, 'value')
          purchasesRoute = route ?? purchasesRoute
          break
        case 'inventory on hand':
          inventoryOnHand = dashboardReportCellNumber(row, columns, 'value')
          inventoryRoute = route ?? inventoryRoute
          break
        case 'gross margin':
          grossMargin = dashboardReportCellNumber(row, columns, 'value')
          grossMarginRoute = route ?? grossMarginRoute
          break
        default:
          break
      }

      continue
    }

    if (category === 'Inventory Position') {
      inventoryPositions.push({
        item: subject || 'Item',
        warehouse: dashboardReportCellDisplay(row, columns, 'secondary') || 'Warehouse',
        quantity: dashboardReportCellNumber(row, columns, 'value'),
        route: resolveReportCellActionUrl(valueCell?.action ?? subjectCell?.action ?? null),
        itemRoute: resolveReportCellActionUrl(subjectCell?.action ?? null),
        warehouseRoute: resolveReportCellActionUrl(secondaryCell?.action ?? null),
      })
      continue
    }

    if (category === 'Top Item') {
      const notes = dashboardReportCellDisplay(row, columns, 'notes')
      const margin = /Gross Margin\s+(-?[\d.]+)\s+\((-?[\d.]+)%\)/i.exec(notes)
      topItems.push({
        item: subject || 'Item',
        soldQuantity: dashboardReportCellNumber(row, columns, 'secondary'),
        netSales: dashboardReportCellNumber(row, columns, 'value'),
        grossMargin: Number(margin?.[1] ?? 0),
        marginPercent: Number(margin?.[2] ?? 0),
        route: resolveReportCellActionUrl(subjectCell?.action ?? null),
      })
      continue
    }

    if (category === 'Top Customer') {
      const counts = /(\d+) sales\s*\/\s*(\d+) returns/i.exec(
        dashboardReportCellDisplay(row, columns, 'secondary'),
      )
      const margin = /Gross Margin\s+(-?[\d.]+)\s+\((-?[\d.]+)%\)/i.exec(
        dashboardReportCellDisplay(row, columns, 'notes'),
      )
      topCustomers.push({
        customer: subject || 'Customer',
        salesDocumentCount: Number(counts?.[1] ?? 0),
        returnDocumentCount: Number(counts?.[2] ?? 0),
        netSales: dashboardReportCellNumber(row, columns, 'value'),
        grossMargin: Number(margin?.[1] ?? 0),
        marginPercent: Number(margin?.[2] ?? 0),
        route: resolveReportCellActionUrl(valueCell?.action ?? subjectCell?.action ?? null),
      })
      continue
    }

    if (category === 'Top Vendor') {
      const counts = /(\d+) purchases\s*\/\s*(\d+) returns/i.exec(
        dashboardReportCellDisplay(row, columns, 'secondary'),
      )
      topVendors.push({
        vendor: subject || 'Vendor',
        purchaseDocumentCount: Number(counts?.[1] ?? 0),
        returnDocumentCount: Number(counts?.[2] ?? 0),
        netPurchases: dashboardReportCellNumber(row, columns, 'value'),
        route: resolveReportCellActionUrl(valueCell?.action ?? subjectCell?.action ?? null),
      })
      continue
    }

    if (category === 'Recent Document') {
      recentDocuments.push({
        title: subject || 'Trade document',
        amountDisplay: dashboardReportCellDisplay(row, columns, 'value') || null,
        documentDate: dashboardReportCellDisplay(row, columns, 'secondary') || null,
        notes: dashboardReportCellDisplay(row, columns, 'notes'),
        route: resolveReportCellActionUrl(subjectCell?.action ?? null),
      })
    }
  }

  return {
    salesThisMonth,
    purchasesThisMonth,
    inventoryOnHand,
    grossMargin,
    inventoryPositionCount: inventoryPositionCount || inventoryPositions.length,
    activeSalesItemCount: Number(response.diagnostics?.active_sales_item_count ?? topItems.length),
    activeCustomerCount: Number(response.diagnostics?.active_customer_count ?? topCustomers.length),
    activeVendorCount: Number(response.diagnostics?.active_vendor_count ?? topVendors.length),
    topItems: topItems.slice(0, 5),
    topCustomers: topCustomers.slice(0, 5),
    topVendors: topVendors.slice(0, 5),
    inventoryPositions: inventoryPositions.slice(0, 8),
    recentDocuments: recentDocuments.slice(0, 8),
    routes: {
      sales: salesRoute,
      purchases: purchasesRoute,
      inventory: inventoryRoute,
      grossMargin: grossMarginRoute,
    },
  }
}

export async function loadHomeDashboard(asOf: string, signal?: AbortSignal): Promise<TradeHomeDashboardData> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Select a valid as-of date.')

  const monthStart = startOfDashboardUtcMonth(asOfDate)
  const fromInclusive = toDashboardUtcDateOnly(monthStart)
  const toInclusive = toDashboardUtcDateOnly(asOfDate)
  const monthKey = toDashboardUtcMonthKey(asOfDate)
  const monthLabel = formatDashboardMonthLabel(monthKey)
  const warnings: string[] = []
  const defaultRoutes = buildDefaultRoutes(fromInclusive, toInclusive, asOf)

  let overviewResult: { value: OverviewSnapshot | null; warning: string | null }
  try {
    overviewResult = { value: await loadOverviewSnapshot(asOf, defaultRoutes, signal), warning: null }
  } catch (error) {
    if (signal?.aborted) throw error
    overviewResult = {
      value: null,
      warning: `Overview analytics are unavailable: ${error instanceof Error ? error.message : String(error)}`,
    }
  }

  if (overviewResult.warning) warnings.push(overviewResult.warning)

  const overview = overviewResult.value ?? {
    salesThisMonth: 0,
    purchasesThisMonth: 0,
    inventoryOnHand: 0,
    grossMargin: 0,
    inventoryPositionCount: 0,
    activeSalesItemCount: 0,
    activeCustomerCount: 0,
    activeVendorCount: 0,
    topItems: [],
    topCustomers: [],
    topVendors: [],
    inventoryPositions: [],
    recentDocuments: [],
    routes: {
      sales: defaultRoutes.sales,
      purchases: defaultRoutes.purchases,
      inventory: defaultRoutes.inventory,
      grossMargin: defaultRoutes.grossMargin,
    },
  }
  return {
    warnings,
    asOf,
    monthKey,
    monthLabel,
    salesThisMonth: overview.salesThisMonth,
    purchasesThisMonth: overview.purchasesThisMonth,
    inventoryOnHand: overview.inventoryOnHand,
    grossMargin: overview.grossMargin,
    activeSalesItemCount: overview.activeSalesItemCount,
    activeCustomerCount: overview.activeCustomerCount,
    activeVendorCount: overview.activeVendorCount,
    inventoryPositionCount: overview.inventoryPositionCount,
    topItems: overview.topItems,
    topCustomers: overview.topCustomers,
    topVendors: overview.topVendors,
    inventoryPositions: overview.inventoryPositions,
    recentDocuments: overview.recentDocuments,
    charts: {
      salesMix: {
        title: 'Sales mix by item',
        subtitle: 'Net sales and gross margin for the top-selling items this month',
        labels: overview.topItems.map((item) => item.item),
        series: [
          { label: 'Net sales', color: 'var(--ngb-blue)', values: overview.topItems.map((item) => item.netSales) },
          { label: 'Gross margin', color: 'var(--ngb-accent-1)', values: overview.topItems.map((item) => item.grossMargin) },
        ],
        route: defaultRoutes.salesByItem,
      },
      inventoryFootprint: {
        title: 'Inventory footprint',
        subtitle: 'Largest on-hand positions across item and warehouse combinations',
        labels: overview.inventoryPositions.map((position) => `${position.item} · ${position.warehouse}`),
        series: [
          { label: 'Quantity', color: 'var(--ngb-accent-2)', values: overview.inventoryPositions.map((position) => position.quantity) },
        ],
        route: defaultRoutes.inventory,
      },
    },
    routes: {
      sales: overview.routes.sales,
      purchases: overview.routes.purchases,
      inventory: overview.routes.inventory,
      grossMargin: overview.routes.grossMargin,
      currentPrices: defaultRoutes.currentPrices,
      salesByItem: defaultRoutes.salesByItem,
      salesByCustomer: defaultRoutes.salesByCustomer,
      purchasesByVendor: defaultRoutes.purchasesByVendor,
    },
  }
}
