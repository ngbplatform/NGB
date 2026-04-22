import {
  buildReportPageUrl,
  captureDashboardValue,
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
} from 'ngb-ui-framework'

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
  inventoryPositions: TradeHomeInventoryPosition[]
  recentDocuments: TradeHomeRecentDocument[]
  routes: Pick<TradeHomeRoutes, 'sales' | 'purchases' | 'inventory' | 'grossMargin'>
}

type ItemSnapshot = {
  totalCount: number
  items: TradeHomeTopItem[]
}

type CustomerSnapshot = {
  totalCount: number
  customers: TradeHomeTopCustomer[]
}

type VendorSnapshot = {
  totalCount: number
  vendors: TradeHomeTopVendor[]
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

function compareDescending(left: number, right: number): number {
  return right - left
}

function buildMonthRequest(fromInclusive: string, toInclusive: string): ReportExecutionRequestDto {
  return {
    parameters: {
      from_utc: fromInclusive,
      to_utc: toInclusive,
    },
    layout: null,
    filters: null,
    variantCode: null,
    offset: 0,
    limit: 500,
    cursor: null,
  }
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
): Promise<OverviewSnapshot> {
  const response = await executeReport(REPORTS.dashboardOverview, buildAsOfRequest(asOf))
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

async function loadTopItemsSnapshot(
  fromInclusive: string,
  toInclusive: string,
): Promise<ItemSnapshot> {
  const response = await executeReport(REPORTS.salesByItem, buildMonthRequest(fromInclusive, toInclusive))
  const columns = dashboardReportColumnIndexMap(response)
  const items = reportDetailRows(response)
    .map((row) => {
      const itemCell = dashboardReportCellByCode(row, columns, 'item')
      return {
        item: dashboardReportCellDisplay(row, columns, 'item') || 'Item',
        soldQuantity: dashboardReportCellNumber(row, columns, 'sold_quantity'),
        netSales: dashboardReportCellNumber(row, columns, 'net_sales'),
        grossMargin: dashboardReportCellNumber(row, columns, 'gross_margin'),
        marginPercent: dashboardReportCellNumber(row, columns, 'margin_percent'),
        route: resolveReportCellActionUrl(itemCell?.action ?? null),
      }
    })
    .sort((left, right) =>
      compareDescending(left.netSales, right.netSales)
      || compareDescending(left.grossMargin, right.grossMargin)
      || left.item.localeCompare(right.item))

  return {
    totalCount: typeof response.total === 'number' ? response.total : items.length,
    items: items.slice(0, 5),
  }
}

async function loadTopCustomersSnapshot(
  fromInclusive: string,
  toInclusive: string,
): Promise<CustomerSnapshot> {
  const response = await executeReport(REPORTS.salesByCustomer, buildMonthRequest(fromInclusive, toInclusive))
  const columns = dashboardReportColumnIndexMap(response)
  const customers = reportDetailRows(response)
    .map((row) => {
      const customerCell = dashboardReportCellByCode(row, columns, 'customer')
      const netSalesCell = dashboardReportCellByCode(row, columns, 'net_sales')

      return {
        customer: dashboardReportCellDisplay(row, columns, 'customer') || 'Customer',
        salesDocumentCount: dashboardReportCellNumber(row, columns, 'sales_document_count'),
        returnDocumentCount: dashboardReportCellNumber(row, columns, 'return_document_count'),
        netSales: dashboardReportCellNumber(row, columns, 'net_sales'),
        grossMargin: dashboardReportCellNumber(row, columns, 'gross_margin'),
        marginPercent: dashboardReportCellNumber(row, columns, 'margin_percent'),
        route: resolveReportCellActionUrl(netSalesCell?.action ?? customerCell?.action ?? null),
      }
    })
    .sort((left, right) =>
      compareDescending(left.netSales, right.netSales)
      || compareDescending(left.grossMargin, right.grossMargin)
      || left.customer.localeCompare(right.customer))

  return {
    totalCount: typeof response.total === 'number' ? response.total : customers.length,
    customers: customers.slice(0, 5),
  }
}

async function loadTopVendorsSnapshot(
  fromInclusive: string,
  toInclusive: string,
): Promise<VendorSnapshot> {
  const response = await executeReport(REPORTS.purchasesByVendor, buildMonthRequest(fromInclusive, toInclusive))
  const columns = dashboardReportColumnIndexMap(response)
  const vendors = reportDetailRows(response)
    .map((row) => {
      const vendorCell = dashboardReportCellByCode(row, columns, 'vendor')

      return {
        vendor: dashboardReportCellDisplay(row, columns, 'vendor') || 'Vendor',
        purchaseDocumentCount: dashboardReportCellNumber(row, columns, 'purchase_document_count'),
        returnDocumentCount: dashboardReportCellNumber(row, columns, 'return_document_count'),
        netPurchases: dashboardReportCellNumber(row, columns, 'net_purchases'),
        route: resolveReportCellActionUrl(vendorCell?.action ?? null),
      }
    })
    .sort((left, right) =>
      compareDescending(left.netPurchases, right.netPurchases)
      || left.vendor.localeCompare(right.vendor))

  return {
    totalCount: typeof response.total === 'number' ? response.total : vendors.length,
    vendors: vendors.slice(0, 5),
  }
}

export async function loadHomeDashboard(asOf: string): Promise<TradeHomeDashboardData> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Select a valid as-of date.')

  const monthStart = startOfDashboardUtcMonth(asOfDate)
  const fromInclusive = toDashboardUtcDateOnly(monthStart)
  const toInclusive = toDashboardUtcDateOnly(asOfDate)
  const monthKey = toDashboardUtcMonthKey(asOfDate)
  const monthLabel = formatDashboardMonthLabel(monthKey)
  const warnings: string[] = []
  const defaultRoutes = buildDefaultRoutes(fromInclusive, toInclusive, asOf)

  const [
    overviewResult,
    topItemsResult,
    topCustomersResult,
    topVendorsResult,
  ] = await Promise.all([
    captureDashboardValue('Overview analytics are unavailable', () => loadOverviewSnapshot(asOf, defaultRoutes)),
    captureDashboardValue('Sales by item analytics are unavailable', () => loadTopItemsSnapshot(fromInclusive, toInclusive)),
    captureDashboardValue('Sales by customer analytics are unavailable', () => loadTopCustomersSnapshot(fromInclusive, toInclusive)),
    captureDashboardValue('Purchases by vendor analytics are unavailable', () => loadTopVendorsSnapshot(fromInclusive, toInclusive)),
  ])

  if (overviewResult.warning) warnings.push(overviewResult.warning)
  if (topItemsResult.warning) warnings.push(topItemsResult.warning)
  if (topCustomersResult.warning) warnings.push(topCustomersResult.warning)
  if (topVendorsResult.warning) warnings.push(topVendorsResult.warning)

  const overview = overviewResult.value ?? {
    salesThisMonth: 0,
    purchasesThisMonth: 0,
    inventoryOnHand: 0,
    grossMargin: 0,
    inventoryPositionCount: 0,
    inventoryPositions: [],
    recentDocuments: [],
    routes: {
      sales: defaultRoutes.sales,
      purchases: defaultRoutes.purchases,
      inventory: defaultRoutes.inventory,
      grossMargin: defaultRoutes.grossMargin,
    },
  }
  const topItems = topItemsResult.value ?? { totalCount: 0, items: [] }
  const topCustomers = topCustomersResult.value ?? { totalCount: 0, customers: [] }
  const topVendors = topVendorsResult.value ?? { totalCount: 0, vendors: [] }

  return {
    warnings,
    asOf,
    monthKey,
    monthLabel,
    salesThisMonth: overview.salesThisMonth,
    purchasesThisMonth: overview.purchasesThisMonth,
    inventoryOnHand: overview.inventoryOnHand,
    grossMargin: overview.grossMargin,
    activeSalesItemCount: topItems.totalCount,
    activeCustomerCount: topCustomers.totalCount,
    activeVendorCount: topVendors.totalCount,
    inventoryPositionCount: overview.inventoryPositionCount,
    topItems: topItems.items,
    topCustomers: topCustomers.customers,
    topVendors: topVendors.vendors,
    inventoryPositions: overview.inventoryPositions,
    recentDocuments: overview.recentDocuments,
    charts: {
      salesMix: {
        title: 'Sales mix by item',
        subtitle: 'Net sales and gross margin for the top-selling items this month',
        labels: topItems.items.map((item) => item.item),
        series: [
          { label: 'Net sales', color: 'var(--ngb-blue)', values: topItems.items.map((item) => item.netSales) },
          { label: 'Gross margin', color: 'var(--ngb-accent-1)', values: topItems.items.map((item) => item.grossMargin) },
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
