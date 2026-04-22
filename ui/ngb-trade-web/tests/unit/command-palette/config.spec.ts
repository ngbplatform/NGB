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

vi.mock('../../../src/command-palette/tradeStaticItems', () => ({
  buildTradeHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
  resolveTradeReportPaletteIcon: vi.fn(({ reportCode }: { reportCode?: string | null }) => `icon:${String(reportCode ?? '')}`),
  TRADE_CREATE_COMMAND_ITEMS: [{ key: 'create:1' }],
  TRADE_FAVORITE_ITEMS: [{ key: 'favorite:1' }],
  TRADE_SPECIAL_PAGE_ITEMS: [{ key: 'special:1' }],
}))

import { createTradeCommandPaletteConfig } from '../../../src/command-palette/config'

describe('trade command palette config', () => {
  beforeEach(() => {
    mocks.buildReportPageUrl.mockClear()
    mocks.getReportDefinitions.mockReset()
    mocks.searchCommandPalette.mockClear()
  })

  it('exposes stable top-level configuration hooks', () => {
    const router = { currentRoute: { value: { fullPath: '/home' } } }
    const config = createTradeCommandPaletteConfig(router as never)

    expect(config.router).toBe(router)
    expect(config.recentStorageKey).toBe('ngb:trade:command-palette:recent')
    expect(config.favoriteItems).toEqual([{ key: 'favorite:1' }])
    expect(config.createItems).toEqual([{ key: 'create:1' }])
    expect(config.specialPageItems).toEqual([{ key: 'special:1' }])
  })

  it('passes remote search through to the framework search api', async () => {
    const config = createTradeCommandPaletteConfig({} as never)

    await config.searchRemote?.({ query: 'invoice', scope: 'all' } as never)

    expect(mocks.searchCommandPalette).toHaveBeenCalledWith({ query: 'invoice', scope: 'all' })
  })

  it('loads report items, filters internal accounting diagnostics, and builds friendly subtitles', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'trd.sales_by_item', name: 'Sales by Item', group: 'Sales', description: 'Margin by SKU' },
      { reportCode: 'accounting.posting_log', name: 'Posting Log', group: 'Diagnostics', description: 'Hidden' },
      { reportCode: 'accounting.consistency', name: 'Consistency', group: 'Diagnostics', description: 'Hidden' },
      { reportCode: 'trd.current_item_prices', name: 'Current Item Prices', group: '', description: '' },
    ])

    const config = createTradeCommandPaletteConfig({} as never)
    const items = await config.loadReportItems?.()

    expect(items).toEqual([
      expect.objectContaining({
        key: 'report:trd.sales_by_item',
        title: 'Sales by Item',
        subtitle: 'Sales · Margin by SKU',
        icon: 'icon:trd.sales_by_item',
        route: '/reports/trd.sales_by_item',
        defaultRank: 700,
      }),
      expect.objectContaining({
        key: 'report:trd.current_item_prices',
        title: 'Current Item Prices',
        subtitle: 'Run this report',
        icon: 'icon:trd.current_item_prices',
        route: '/reports/trd.current_item_prices',
        defaultRank: 699,
      }),
    ])
  })

  it('includes report metadata as keywords for search relevance', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'trd.sales_by_customer', name: 'Sales by Customer', group: 'Sales', description: 'Top accounts' },
    ])

    const config = createTradeCommandPaletteConfig({} as never)
    const [item] = await config.loadReportItems?.() ?? []

    expect(item?.keywords).toEqual(['trd.sales_by_customer', 'Sales', 'Top accounts'])
  })
})
