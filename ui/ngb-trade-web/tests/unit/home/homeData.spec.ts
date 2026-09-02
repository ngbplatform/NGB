import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
  executeReport: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildReportPageUrl: mocks.buildReportPageUrl,
  captureDashboardValue: async <T>(label: string, work: () => Promise<T>) => {
    try {
      return { value: await work(), warning: null }
    } catch (error) {
      return { value: null, warning: `${label}: ${error instanceof Error ? error.message : String(error)}` }
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
    const value = index === undefined ? 0 : Number(row.cells?.[index]?.value ?? row.cells?.[index]?.display ?? 0)
    return Number.isFinite(value) ? value : 0
  },
  executeReport: mocks.executeReport,
  formatDashboardMonthLabel: () => 'Apr 2026',
  isDashboardReportRowKind: (row: { rowKind?: string }, kind: string) => row.rowKind === kind,
  parseDashboardUtcDateOnly: (input: string) => /^\d{4}-\d{2}-\d{2}$/.test(input) ? new Date(`${input}T00:00:00Z`) : null,
  ReportRowKind: { Detail: 'Detail' },
  resolveReportCellActionUrl: (action?: { url?: string | null } | null) => String(action?.url ?? '').trim() || null,
  startOfDashboardUtcMonth: (date: Date) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)),
  toDashboardUtcDateOnly: (date: Date) => date.toISOString().slice(0, 10),
  toDashboardUtcMonthKey: (date: Date) => date.toISOString().slice(0, 7),
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

type Cell = { display?: string | null; value?: number | string | null; action?: { url?: string | null } | null }

function detail(category: string, subject: string, value: Cell = {}, secondary: Cell = {}, notes: Cell = {}) {
  return { rowKind: 'Detail', cells: [{ display: category }, { display: subject }, value, secondary, notes] }
}

function overview() {
  return {
    sheet: {
      columns: ['category', 'subject', 'value', 'secondary', 'notes'].map((code) => ({ code })),
      rows: [
        detail('KPI', 'Sales This Month', { value: 180, action: { url: '/reports/customers' } }),
        detail('KPI', 'Purchases This Month', { value: 95 }),
        detail('KPI', 'Inventory On Hand', { value: 12, action: { url: '/reports/inventory' } }),
        detail('KPI', 'Gross Margin', { value: 55 }),
        detail('Top Item', 'Cable Ties', { value: 90 }, { value: 5 }, { display: 'Gross Margin 20 (22.22%)' }),
        detail('Top Customer', 'Bayview Stores', { value: 180, action: { url: '/reports/customers/bayview' } }, { display: '3 sales / 1 returns' }, { display: 'Gross Margin 55 (30.56%)' }),
        detail('Top Vendor', 'Northstar', { value: 95 }, { display: '2 purchases / 0 returns' }),
        detail('Inventory Position', 'Cable Ties', { value: 8, action: { url: '/reports/inventory?item=a' } }, { display: 'Alpha DC', action: { url: '/catalogs/trd.warehouse/a' } }),
        detail('Recent Document', 'SI-2048', { display: '$80' }, { display: '2026-04-18' }, { display: 'Posted' }),
      ],
    },
    diagnostics: {
      inventory_position_count: '9',
      active_sales_item_count: '6',
      active_customer_count: '4',
      active_vendor_count: '3',
    },
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.executeReport.mockResolvedValue(overview())
})

describe('trade home data', () => {
  it('assembles every dashboard slice from one bounded overview report', async () => {
    const data = await loadHomeDashboard('2026-04-18')

    expect(mocks.executeReport).toHaveBeenCalledOnce()
    expect(mocks.executeReport.mock.calls[0]?.[0]).toBe('trd.dashboard_overview')
    expect(data).toMatchObject({
      monthKey: '2026-04',
      salesThisMonth: 180,
      purchasesThisMonth: 95,
      inventoryOnHand: 12,
      grossMargin: 55,
      inventoryPositionCount: 9,
      activeSalesItemCount: 6,
      activeCustomerCount: 4,
      activeVendorCount: 3,
    })
    expect(data.topItems[0]).toMatchObject({ item: 'Cable Ties', soldQuantity: 5, netSales: 90, grossMargin: 20, marginPercent: 22.22 })
    expect(data.topCustomers[0]).toMatchObject({ customer: 'Bayview Stores', salesDocumentCount: 3, returnDocumentCount: 1, grossMargin: 55 })
    expect(data.topVendors[0]).toMatchObject({ vendor: 'Northstar', purchaseDocumentCount: 2, netPurchases: 95 })
    expect(data.inventoryPositions[0]).toMatchObject({ item: 'Cable Ties', warehouse: 'Alpha DC', quantity: 8 })
    expect(data.recentDocuments[0]).toMatchObject({ title: 'SI-2048', amountDisplay: '$80', documentDate: '2026-04-18' })
    expect(data.charts.salesMix.series[0]?.values).toEqual([90])
    expect(data.warnings).toEqual([])
  })

  it('uses row counts when diagnostics are absent and forwards cancellation', async () => {
    const payload = overview()
    payload.diagnostics = {} as typeof payload.diagnostics
    mocks.executeReport.mockResolvedValue(payload)
    const controller = new AbortController()

    const data = await loadHomeDashboard('2026-04-18', controller.signal)

    expect(data).toMatchObject({ activeSalesItemCount: 1, activeCustomerCount: 1, activeVendorCount: 1 })
    expect(mocks.executeReport).toHaveBeenCalledWith(
      'trd.dashboard_overview',
      expect.any(Object),
      { signal: controller.signal },
    )
  })

  it('returns bounded safe defaults when the overview is unavailable', async () => {
    mocks.executeReport.mockRejectedValue(new Error('overview offline'))

    const data = await loadHomeDashboard('2026-04-18')

    expect(data.warnings).toEqual(['Overview analytics are unavailable: overview offline'])
    expect(data).toMatchObject({
      salesThisMonth: 0,
      activeSalesItemCount: 0,
      activeCustomerCount: 0,
      activeVendorCount: 0,
      topItems: [],
      topCustomers: [],
      topVendors: [],
      inventoryPositions: [],
      recentDocuments: [],
    })
  })

  it('rejects invalid as-of dates before issuing a report request', async () => {
    await expect(loadHomeDashboard('04/18/2026')).rejects.toThrow('Select a valid as-of date.')
    expect(mocks.executeReport).not.toHaveBeenCalled()
  })
})
