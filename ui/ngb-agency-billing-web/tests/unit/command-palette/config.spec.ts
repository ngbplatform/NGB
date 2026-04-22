import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
  getReportDefinitions: vi.fn(),
  searchCommandPalette: vi.fn(async () => [{ key: 'remote:1' }]),
}))

vi.mock('ngb-ui-framework', () => ({
  buildReportPageUrl: mocks.buildReportPageUrl,
  getReportDefinitions: mocks.getReportDefinitions,
  searchCommandPalette: mocks.searchCommandPalette,
}))

vi.mock('../../../src/command-palette/agencyBillingStaticItems', () => ({
  buildAgencyBillingHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
  resolveAgencyBillingReportPaletteIcon: vi.fn(({ reportCode }: { reportCode?: string | null }) => `icon:${String(reportCode ?? '')}`),
  AGENCY_BILLING_CREATE_COMMAND_ITEMS: [{ key: 'create:1' }],
  AGENCY_BILLING_FAVORITE_ITEMS: [{ key: 'favorite:1' }],
  AGENCY_BILLING_SPECIAL_PAGE_ITEMS: [{ key: 'special:1' }],
}))

import { createAgencyBillingCommandPaletteConfig } from '../../../src/command-palette/config'

describe('agency billing command palette config', () => {
  beforeEach(() => {
    mocks.buildReportPageUrl.mockClear()
    mocks.getReportDefinitions.mockReset()
    mocks.searchCommandPalette.mockClear()
  })

  it('exposes stable top-level configuration hooks', () => {
    const router = { currentRoute: { value: { fullPath: '/home' } } }
    const config = createAgencyBillingCommandPaletteConfig(router as never)

    expect(config.router).toBe(router)
    expect(config.recentStorageKey).toBe('ngb:agency-billing:command-palette:recent')
    expect(config.favoriteItems).toEqual([{ key: 'favorite:1' }])
    expect(config.createItems).toEqual([{ key: 'create:1' }])
    expect(config.specialPageItems).toEqual([{ key: 'special:1' }])
  })

  it('passes remote search through to the framework api', async () => {
    const config = createAgencyBillingCommandPaletteConfig({} as never)

    await config.searchRemote?.({ query: 'invoice', scope: 'all' } as never)

    expect(mocks.searchCommandPalette).toHaveBeenCalledWith({ query: 'invoice', scope: 'all' })
  })

  it('loads agency billing reports, filters diagnostics, and builds friendly subtitles', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'ab.invoice_register', name: 'Invoice Register', group: 'Receivables', description: 'Issued and open invoices' },
      { reportCode: 'accounting.posting_log', name: 'Posting Log', group: 'Diagnostics', description: 'Hidden' },
      { reportCode: 'accounting.consistency', name: 'Consistency', group: 'Diagnostics', description: 'Hidden' },
      { reportCode: 'ab.ar_aging', name: 'AR Aging', group: '', description: '' },
      { reportCode: 'trd.sales_by_item', name: 'Trade Report', group: 'Trade', description: 'Ignored' },
    ])

    const config = createAgencyBillingCommandPaletteConfig({} as never)
    const items = await config.loadReportItems?.()

    expect(items).toEqual([
      expect.objectContaining({
        key: 'report:ab.invoice_register',
        title: 'Invoice Register',
        subtitle: 'Receivables · Issued and open invoices',
        icon: 'icon:ab.invoice_register',
        route: '/reports/ab.invoice_register',
        defaultRank: 700,
      }),
      expect.objectContaining({
        key: 'report:ab.ar_aging',
        title: 'AR Aging',
        subtitle: 'Run this report',
        icon: 'icon:ab.ar_aging',
        route: '/reports/ab.ar_aging',
        defaultRank: 699,
      }),
    ])
  })

  it('includes report metadata as keywords for search relevance', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'ab.project_profitability', name: 'Project Profitability', group: 'Margin', description: 'By engagement' },
    ])

    const config = createAgencyBillingCommandPaletteConfig({} as never)
    const [item] = await config.loadReportItems?.() ?? []

    expect(item?.keywords).toEqual(['ab.project_profitability', 'Margin', 'By engagement'])
  })
})
