import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildDocumentFullPageUrl: vi.fn((documentType: string) => `/documents/${documentType}/new`),
  buildNgbHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
}))

vi.mock('ngb-ui-framework', () => ({
  buildDocumentFullPageUrl: mocks.buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions: mocks.buildNgbHeuristicCurrentActions,
  buildReportPageUrl: mocks.buildReportPageUrl,
  NGB_ACCOUNTING_CREATE_ITEMS: [{ key: 'accounting:create', route: '/accounting/create' }],
  NGB_ACCOUNTING_FAVORITE_ITEMS: [{ key: 'accounting:favorite', route: '/accounting/favorite' }],
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS: [{ key: 'accounting:special', route: '/accounting/special' }],
}))

import {
  buildTradeHeuristicCurrentActions,
  resolveTradeReportPaletteIcon,
  TRADE_CREATE_COMMAND_ITEMS,
  TRADE_FAVORITE_ITEMS,
  TRADE_SPECIAL_PAGE_ITEMS,
} from '../../../src/command-palette/tradeStaticItems'

describe('trade static command palette items', () => {
  beforeEach(() => {
    mocks.buildDocumentFullPageUrl.mockClear()
    mocks.buildNgbHeuristicCurrentActions.mockClear()
    mocks.buildReportPageUrl.mockClear()
  })

  it('delegates heuristic action discovery with trade-specific exclusions', () => {
    expect(buildTradeHeuristicCurrentActions('/documents/trd.sales_invoice')).toEqual([{ key: 'heuristic:/documents/trd.sales_invoice' }])
    expect(mocks.buildNgbHeuristicCurrentActions).toHaveBeenCalledWith('/documents/trd.sales_invoice', {
      excludedCatalogTypes: ['trd.accounting_policy'],
    })
  })

  it.each([
    ['trd.dashboard_overview', 'home'],
    ['trd.inventory_balances', 'bar-chart'],
    ['trd.inventory_movements', 'bar-chart'],
    ['trd.current_item_prices', 'bar-chart'],
    ['trd.sales_by_item', 'bar-chart'],
    ['trd.sales_by_customer', 'bar-chart'],
    ['trd.purchases_by_vendor', 'bar-chart'],
    ['accounting.general_journal', 'receipt'],
    ['unknown.report', 'bar-chart'],
  ])('resolves report icon for %s', (reportCode, expectedIcon) => {
    expect(resolveTradeReportPaletteIcon({ reportCode })).toBe(expectedIcon)
  })

  it.each([
    ['create:purchase-receipt', 'Create Purchase Receipt', '/documents/trd.purchase_receipt/new'],
    ['create:sales-invoice', 'Create Sales Invoice', '/documents/trd.sales_invoice/new'],
    ['create:customer-payment', 'Create Customer Payment', '/documents/trd.customer_payment/new'],
    ['create:vendor-payment', 'Create Vendor Payment', '/documents/trd.vendor_payment/new'],
    ['create:inventory-transfer', 'Create Inventory Transfer', '/documents/trd.inventory_transfer/new'],
    ['create:inventory-adjustment', 'Create Inventory Adjustment', '/documents/trd.inventory_adjustment/new'],
    ['create:item-price-update', 'Create Item Price Update', '/documents/trd.item_price_update/new'],
  ])('publishes trade create item %s', (key, title, route) => {
    expect(TRADE_CREATE_COMMAND_ITEMS).toContainEqual(expect.objectContaining({
      key,
      title,
      route,
      badge: 'Create',
      icon: 'plus',
      openInNewTabSupported: true,
    }))
  })

  it('keeps accounting create commands appended after trade commands', () => {
    expect(TRADE_CREATE_COMMAND_ITEMS.at(-1)).toEqual({ key: 'accounting:create', route: '/accounting/create' })
  })

  it.each([
    ['page:home', '/home', 'Overview'],
    ['favorite:sales-invoices', '/documents/trd.sales_invoice', 'Review outbound sales documents'],
    ['favorite:purchase-receipts', '/documents/trd.purchase_receipt', 'Track inbound stock receipts'],
    ['favorite:inventory-balances', '/reports/trd.inventory_balances', 'See on-hand quantity by item and warehouse'],
    ['favorite:current-prices', '/reports/trd.current_item_prices', 'Review the active price book'],
  ])('publishes favorite item %s', (key, route, subtitle) => {
    expect(TRADE_FAVORITE_ITEMS).toContainEqual(expect.objectContaining({
      key,
      route,
      subtitle,
      openInNewTabSupported: true,
    }))
  })

  it('keeps accounting favorite items appended after trade favorites', () => {
    expect(TRADE_FAVORITE_ITEMS.at(-1)).toEqual({ key: 'accounting:favorite', route: '/accounting/favorite' })
  })

  it.each([
    ['page:dashboard', '/home', 'Overview'],
    ['page:accounting-policy', '/catalogs/trd.accounting_policy', 'Setup & Controls'],
  ])('publishes special page item %s', (key, route, subtitle) => {
    expect(TRADE_SPECIAL_PAGE_ITEMS).toContainEqual(expect.objectContaining({
      key,
      route,
      subtitle,
      badge: 'Page',
    }))
  })

  it('keeps accounting special pages merged into the trade surface', () => {
    expect(TRADE_SPECIAL_PAGE_ITEMS[0]).toEqual({ key: 'accounting:special', route: '/accounting/special' })
  })
})
