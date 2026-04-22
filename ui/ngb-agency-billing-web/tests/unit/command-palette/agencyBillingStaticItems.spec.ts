import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildDocumentFullPageUrl: vi.fn((documentType: string) => `/documents/${documentType}/new`),
  buildNgbHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
}))

vi.mock('ngb-ui-framework', () => ({
  buildDocumentFullPageUrl: mocks.buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions: mocks.buildNgbHeuristicCurrentActions,
  NGB_ACCOUNTING_CREATE_ITEMS: [{ key: 'accounting:create', route: '/accounting/create' }],
  NGB_ACCOUNTING_FAVORITE_ITEMS: [{ key: 'accounting:favorite', route: '/accounting/favorite' }],
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS: [{ key: 'accounting:special', route: '/accounting/special' }],
}))

import {
  AGENCY_BILLING_CREATE_COMMAND_ITEMS,
  AGENCY_BILLING_FAVORITE_ITEMS,
  AGENCY_BILLING_SPECIAL_PAGE_ITEMS,
  buildAgencyBillingHeuristicCurrentActions,
  resolveAgencyBillingReportPaletteIcon,
} from '../../../src/command-palette/agencyBillingStaticItems'

describe('agency billing static command palette items', () => {
  beforeEach(() => {
    mocks.buildDocumentFullPageUrl.mockClear()
    mocks.buildNgbHeuristicCurrentActions.mockClear()
  })

  it('delegates heuristic action discovery with singleton exclusions', () => {
    expect(buildAgencyBillingHeuristicCurrentActions('/documents/ab.sales_invoice')).toEqual([
      { key: 'heuristic:/documents/ab.sales_invoice' },
    ])
    expect(mocks.buildNgbHeuristicCurrentActions).toHaveBeenCalledWith('/documents/ab.sales_invoice', {
      excludedCatalogTypes: ['ab.accounting_policy'],
    })
  })

  it.each([
    ['ab.dashboard_overview', 'home'],
    ['ab.unbilled_time_report', 'calendar-check'],
    ['ab.invoice_register', 'receipt'],
    ['ab.ar_aging', 'book-open'],
    ['ab.team_utilization', 'users'],
    ['ab.project_profitability', 'bar-chart'],
    ['unknown.report', 'bar-chart'],
  ])('resolves report icon for %s', (reportCode, expectedIcon) => {
    expect(resolveAgencyBillingReportPaletteIcon({ reportCode })).toBe(expectedIcon)
  })

  it('publishes the core agency create commands before accounting commands', () => {
    expect(AGENCY_BILLING_CREATE_COMMAND_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'create:client-contract', route: '/documents/ab.client_contract/new', badge: 'Create' }),
      expect.objectContaining({ key: 'create:timesheet', route: '/documents/ab.timesheet/new', badge: 'Create' }),
      expect.objectContaining({ key: 'create:sales-invoice', route: '/documents/ab.sales_invoice/new', badge: 'Create' }),
      expect.objectContaining({ key: 'create:customer-payment', route: '/documents/ab.customer_payment/new', badge: 'Create' }),
    ]))
    expect(AGENCY_BILLING_CREATE_COMMAND_ITEMS.at(-1)).toEqual({ key: 'accounting:create', route: '/accounting/create' })
  })

  it('publishes the core agency favorites before accounting favorites', () => {
    expect(AGENCY_BILLING_FAVORITE_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'page:home', route: '/home', subtitle: 'Operational overview' }),
      expect.objectContaining({ key: 'page:timesheets', route: '/documents/ab.timesheet', subtitle: 'Capture and review billable time' }),
      expect.objectContaining({ key: 'page:sales-invoices', route: '/documents/ab.sales_invoice', subtitle: 'Prepare and track invoices' }),
      expect.objectContaining({ key: 'page:customer-payments', route: '/documents/ab.customer_payment', subtitle: 'Record incoming cash' }),
    ]))
    expect(AGENCY_BILLING_FAVORITE_ITEMS.at(-1)).toEqual({ key: 'accounting:favorite', route: '/accounting/favorite' })
  })

  it('keeps singleton setup pages available inside the special pages surface', () => {
    expect(AGENCY_BILLING_SPECIAL_PAGE_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'page:dashboard', route: '/home', badge: 'Page' }),
      expect.objectContaining({ key: 'page:team-members', route: '/catalogs/ab.team_member', badge: 'Page' }),
      expect.objectContaining({ key: 'page:projects', route: '/catalogs/ab.project', badge: 'Page' }),
      expect.objectContaining({ key: 'page:rate-cards', route: '/catalogs/ab.rate_card', badge: 'Page' }),
      expect.objectContaining({ key: 'page:accounting-policy', route: '/catalogs/ab.accounting_policy', subtitle: 'Setup & controls' }),
    ]))
    expect(AGENCY_BILLING_SPECIAL_PAGE_ITEMS[0]).toEqual({ key: 'accounting:special', route: '/accounting/special' })
  })
})
