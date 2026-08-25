import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildReportPageUrl: vi.fn((reportCode: string, args?: { context?: { request?: { parameters?: Record<string, string> | null } | null } | null }) => {
    const params = args?.context?.request?.parameters
    const query = params ? `?${new URLSearchParams(params).toString()}` : ''
    return `/reports/${reportCode}${query}`
  }),
  executeReport: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildReportPageUrl: mocks.buildReportPageUrl,
  captureDashboardValue: async <T>(label: string, work: () => Promise<T>) => {
    try {
      return { value: await work(), warning: null }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      return { value: null, warning: `${label}: ${message}` }
    }
  },
  dashboardReportColumnIndexMap: (response: { sheet?: { columns?: Array<{ code: string }> } }) =>
    new Map((response.sheet?.columns ?? []).map((column, index) => [column.code, index] as const)),
  dashboardReportCellByCode: (row: { cells?: unknown[] }, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    return index === undefined ? null : row.cells?.[index] ?? null
  },
  dashboardReportCellDisplay: (row: { cells?: Array<{ display?: string | null }> }, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    return index === undefined ? '' : String(row.cells?.[index]?.display ?? '').trim()
  },
  dashboardReportCellNumber: (row: { cells?: Array<{ value?: number | string | null; display?: string | null }> }, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    if (index === undefined) return 0
    const cell = row.cells?.[index]
    const value = Number(cell?.value ?? cell?.display ?? 0)
    return Number.isFinite(value) ? value : 0
  },
  executeReport: mocks.executeReport,
  formatDashboardMonthLabel: (monthKey: string) => {
    const [year, month] = monthKey.split('-').map(Number)
    const labels = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
    return `${labels[(month ?? 1) - 1]} ${year}`
  },
  isDashboardReportRowKind: (row: { rowKind?: string }, kind: string) => row.rowKind === kind,
  parseDashboardUtcDateOnly: (input: string) => {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(input).trim())
    if (!match) return null
    const [_, year, month, day] = match
    return new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)))
  },
  ReportRowKind: {
    Detail: 'Detail',
  },
  resolveReportCellActionUrl: (action?: { url?: string | null } | null) => String(action?.url ?? '').trim() || null,
  startOfDashboardUtcMonth: (date: Date) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)),
  toDashboardUtcDateOnly: (date: Date) => date.toISOString().slice(0, 10),
  toDashboardUtcMonthKey: (date: Date) => date.toISOString().slice(0, 7),
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

function response(columns: string[], rows: Array<{ rowKind: string; cells: Array<{ display?: string | null; value?: number | string | null; action?: { url?: string | null } | null }> }>, total?: number, diagnostics?: Record<string, string>) {
  return {
    sheet: {
      columns: columns.map((code) => ({ code })),
      rows,
    },
    total,
    diagnostics,
  }
}

describe('trade home data', () => {
  beforeEach(() => {
    mocks.buildReportPageUrl.mockClear()
    mocks.executeReport.mockReset()
  })

  it('assembles the dashboard from overview and ranking reports', async () => {
    mocks.executeReport.mockImplementation(async (reportCode: string) => {
      switch (reportCode) {
        case 'trd.dashboard_overview':
          return response(
            ['category', 'subject', 'value', 'secondary', 'notes'],
            [
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'KPI' },
                  { display: 'Sales This Month' },
                  { display: '$180', value: 180, action: { url: '/reports/customers' } },
                  { display: '2026-04-01 to 2026-04-18' },
                  { display: 'Net invoiced after returns.' },
                ],
              },
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'KPI' },
                  { display: 'Inventory On Hand' },
                  { display: '12', value: 12, action: { url: '/reports/inventory' } },
                  { display: 'As of 2026-04-18' },
                  { display: 'On hand quantity.' },
                ],
              },
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Inventory Position' },
                  { display: 'Cable Ties' },
                  { display: '8', value: 8, action: { url: '/reports/inventory?item=item-a' } },
                  { display: 'Alpha DC', action: { url: '/catalogs/trd.warehouse/alpha' } },
                  { display: '' },
                ],
              },
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Recent Document' },
                  { display: 'Sales Invoice SI-2048', action: { url: '/documents/trd.sales_invoice/si-2048' } },
                  { display: '$80', value: 80 },
                  { display: '2026-04-18' },
                  { display: 'Posted to the general journal' },
                ],
              },
            ],
            undefined,
            { inventory_position_count: '9' },
          )
        case 'trd.sales_by_item':
          return response(
            ['item', 'sold_quantity', 'net_sales', 'gross_margin', 'margin_percent'],
            [
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Cable Ties', action: { url: '/reports/items/cable-ties' } },
                  { display: '5', value: 5 },
                  { display: '60', value: 60 },
                  { display: '20', value: 20 },
                  { display: '33.3', value: 33.3 },
                ],
              },
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Adapter Kit', action: { url: '/reports/items/adapter-kit' } },
                  { display: '4', value: 4 },
                  { display: '90', value: 90 },
                  { display: '10', value: 10 },
                  { display: '11.1', value: 11.1 },
                ],
              },
            ],
            6,
          )
        case 'trd.sales_by_customer':
          return response(
            ['customer', 'sales_document_count', 'return_document_count', 'net_sales', 'gross_margin', 'margin_percent'],
            [
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Bayview Stores', action: { url: '/reports/customers/bayview' } },
                  { display: '3', value: 3 },
                  { display: '1', value: 1 },
                  { display: '180', value: 180, action: { url: '/reports/customers' } },
                  { display: '55', value: 55 },
                  { display: '30.6', value: 30.6 },
                ],
              },
            ],
            4,
          )
        case 'trd.purchases_by_vendor':
          return response(
            ['vendor', 'purchase_document_count', 'return_document_count', 'net_purchases'],
            [
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Northstar Distribution', action: { url: '/reports/vendors/northstar' } },
                  { display: '2', value: 2 },
                  { display: '0', value: 0 },
                  { display: '95', value: 95 },
                ],
              },
            ],
            3,
          )
        default:
          throw new Error(`Unexpected report ${reportCode}`)
      }
    })

    const data = await loadHomeDashboard('2026-04-18')

    expect(data.monthKey).toBe('2026-04')
    expect(data.monthLabel).toBe('Apr 2026')
    expect(data.salesThisMonth).toBe(180)
    expect(data.inventoryOnHand).toBe(12)
    expect(data.inventoryPositionCount).toBe(9)
    expect(data.activeSalesItemCount).toBe(6)
    expect(data.activeCustomerCount).toBe(4)
    expect(data.activeVendorCount).toBe(3)
    expect(data.routes.sales).toBe('/reports/customers')
    expect(data.routes.inventory).toBe('/reports/inventory')
    expect(data.topItems.map((item) => item.item)).toEqual(['Adapter Kit', 'Cable Ties'])
    expect(data.topCustomers[0]?.customer).toBe('Bayview Stores')
    expect(data.topVendors[0]?.vendor).toBe('Northstar Distribution')
    expect(data.inventoryPositions[0]).toMatchObject({
      item: 'Cable Ties',
      warehouse: 'Alpha DC',
      quantity: 8,
      route: '/reports/inventory?item=item-a',
    })
    expect(data.recentDocuments[0]).toMatchObject({
      title: 'Sales Invoice SI-2048',
      amountDisplay: '$80',
      documentDate: '2026-04-18',
      route: '/documents/trd.sales_invoice/si-2048',
    })
    expect(data.charts.salesMix.labels).toEqual(['Adapter Kit', 'Cable Ties'])
    expect(data.charts.salesMix.series[0]?.values).toEqual([90, 60])
    expect(data.warnings).toEqual([])
  })

  it('keeps partial data and emits warnings when one of the report slices fails', async () => {
    mocks.executeReport.mockImplementation(async (reportCode: string) => {
      if (reportCode === 'trd.sales_by_customer') throw new Error('Customer cube timed out')

      if (reportCode === 'trd.dashboard_overview') {
        return response(['category', 'subject', 'value', 'secondary', 'notes'], [])
      }

      if (reportCode === 'trd.sales_by_item') {
        return {
          sheet: {
            columns: ['item', 'sold_quantity', 'net_sales', 'gross_margin', 'margin_percent'].map((code) => ({ code })),
          },
          total: 0,
        }
      }

      return response(['vendor', 'purchase_document_count', 'return_document_count', 'net_purchases'], [], 0)
    })

    const data = await loadHomeDashboard('2026-04-18')

    expect(data.activeCustomerCount).toBe(0)
    expect(data.topCustomers).toEqual([])
    expect(data.warnings).toEqual(['Sales by customer analytics are unavailable: Customer cube timed out'])
    expect(data.routes.currentPrices).toBe('/reports/trd.current_item_prices')
  })

  it('normalizes sparse report cells, invalid diagnostics, and ranking ties', async () => {
    mocks.executeReport.mockImplementation(async (reportCode: string) => {
      switch (reportCode) {
        case 'trd.dashboard_overview':
          return response(
            ['category', 'subject', 'value', 'secondary', 'notes'],
            [
              {
                rowKind: 'Summary',
                cells: [{ display: 'KPI' }, { display: 'Ignored summary' }, { value: 999 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Purchases This Month' }, { value: 25 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Sales This Month' }, { value: 31 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Inventory On Hand' }, { value: 12 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Gross Margin' }, { value: 7, action: { url: '/reports/custom-gross-margin' } }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Gross Margin' }, { value: 8 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'KPI' }, { display: 'Unsupported KPI' }, { value: 123 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'Inventory Position' }, {}, { value: 3 }, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [
                  { display: 'Inventory Position' },
                  { display: 'Adapter Kit', action: { url: '/catalogs/trd.item/adapter' } },
                  { value: 4 },
                  { display: 'Main', action: { url: '/catalogs/trd.warehouse/main' } },
                  {},
                ],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'Recent Document' }, {}, {}, {}, {}],
              },
              {
                rowKind: 'Detail',
                cells: [{ display: 'Unknown category' }, {}, {}, {}, {}],
              },
            ],
            undefined,
            { inventory_position_count: '-1' },
          )
        case 'trd.sales_by_item':
          return response(
            ['item', 'sold_quantity', 'net_sales', 'gross_margin', 'margin_percent'],
            [
              { rowKind: 'Detail', cells: [{ display: 'Zulu' }, {}, { value: 100 }, { value: 10 }, {}] },
              { rowKind: 'Detail', cells: [{ display: 'Alpha' }, {}, { value: 100 }, { value: 10 }, {}] },
              { rowKind: 'Detail', cells: [{ display: 'Higher Margin' }, {}, { value: 100 }, { value: 20 }, {}] },
              { rowKind: 'Detail', cells: [{}, {}, { value: 90 }, { value: 50 }, {}] },
            ],
          )
        case 'trd.sales_by_customer':
          return response(
            ['customer', 'sales_document_count', 'return_document_count', 'net_sales', 'gross_margin', 'margin_percent'],
            [
              { rowKind: 'Detail', cells: [{ display: 'Zulu', action: { url: '/catalogs/trd.customer/zulu' } }, {}, {}, { value: 100 }, { value: 10 }, {}] },
              { rowKind: 'Detail', cells: [{ display: 'Alpha' }, {}, {}, { value: 100 }, { value: 10 }, {}] },
              { rowKind: 'Detail', cells: [{ display: 'Higher Margin' }, {}, {}, { value: 100 }, { value: 20 }, {}] },
              { rowKind: 'Detail', cells: [{}, {}, {}, { value: 90 }, { value: 50 }, {}] },
            ],
          )
        case 'trd.purchases_by_vendor':
          return response(
            ['vendor', 'purchase_document_count', 'return_document_count', 'net_purchases'],
            [
              { rowKind: 'Detail', cells: [{ display: 'Zulu' }, {}, {}, { value: 100 }] },
              { rowKind: 'Detail', cells: [{ display: 'Alpha' }, {}, {}, { value: 100 }] },
              { rowKind: 'Detail', cells: [{}, {}, {}, { value: 90 }] },
            ],
          )
        default:
          throw new Error(`Unexpected report ${reportCode}`)
      }
    })

    const data = await loadHomeDashboard('2026-04-18')

    expect(data.purchasesThisMonth).toBe(25)
    expect(data.grossMargin).toBe(8)
    expect(data.routes.grossMargin).toBe('/reports/custom-gross-margin')
    expect(data.inventoryPositionCount).toBe(2)
    expect(data.inventoryPositions[0]).toMatchObject({
      item: 'Item',
      warehouse: 'Warehouse',
      route: null,
      itemRoute: null,
      warehouseRoute: null,
    })
    expect(data.inventoryPositions[1]).toMatchObject({
      route: '/catalogs/trd.item/adapter',
      itemRoute: '/catalogs/trd.item/adapter',
      warehouseRoute: '/catalogs/trd.warehouse/main',
    })
    expect(data.recentDocuments[0]).toMatchObject({
      title: 'Trade document',
      amountDisplay: null,
      documentDate: null,
      route: null,
    })
    expect(data.activeSalesItemCount).toBe(4)
    expect(data.topItems.map((item) => item.item)).toEqual(['Higher Margin', 'Alpha', 'Zulu', 'Item'])
    expect(data.activeCustomerCount).toBe(4)
    expect(data.topCustomers.map((customer) => customer.customer)).toEqual(['Higher Margin', 'Alpha', 'Zulu', 'Customer'])
    expect(data.topCustomers.find((customer) => customer.customer === 'Zulu')?.route).toBe('/catalogs/trd.customer/zulu')
    expect(data.activeVendorCount).toBe(3)
    expect(data.topVendors.map((vendor) => vendor.vendor)).toEqual(['Alpha', 'Zulu', 'Vendor'])
  })

  it('returns safe defaults and a warning for every unavailable report slice', async () => {
    mocks.executeReport.mockImplementation(async (reportCode: string) => {
      throw new Error(`${reportCode} offline`)
    })

    const data = await loadHomeDashboard('2026-04-18')

    expect(data.warnings).toEqual([
      'Overview analytics are unavailable: trd.dashboard_overview offline',
      'Sales by item analytics are unavailable: trd.sales_by_item offline',
      'Sales by customer analytics are unavailable: trd.sales_by_customer offline',
      'Purchases by vendor analytics are unavailable: trd.purchases_by_vendor offline',
    ])
    expect(data).toMatchObject({
      salesThisMonth: 0,
      purchasesThisMonth: 0,
      inventoryOnHand: 0,
      grossMargin: 0,
      activeSalesItemCount: 0,
      activeCustomerCount: 0,
      activeVendorCount: 0,
      inventoryPositionCount: 0,
      topItems: [],
      topCustomers: [],
      topVendors: [],
      inventoryPositions: [],
      recentDocuments: [],
    })
  })

  it('rejects invalid as-of dates before issuing any report calls', async () => {
    await expect(loadHomeDashboard('04/18/2026')).rejects.toThrow('Select a valid as-of date.')
    expect(mocks.executeReport).not.toHaveBeenCalled()
  })
})
