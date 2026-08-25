import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
  getReportDefinitions: vi.fn(),
  groups: [{ title: 'Main' }],
  searchCommandPalette: vi.fn(async () => [{ key: 'remote:1' }]),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildReportPageUrl: mocks.buildReportPageUrl,
  getReportDefinitions: mocks.getReportDefinitions,
  searchCommandPalette: mocks.searchCommandPalette,
  useMainMenuStore: () => ({ groups: mocks.groups }),
}))

vi.mock('../../../src/command-palette/pmStaticItems', () => ({
  buildPmHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
  resolvePmReportPaletteIcon: vi.fn(({ reportCode }: { reportCode?: string | null }) => `icon:${String(reportCode ?? '')}`),
  PM_CREATE_COMMAND_ITEMS: [{ key: 'create:1' }],
  PM_FAVORITE_ITEMS: [{ key: 'favorite:1' }],
  PM_SPECIAL_PAGE_ITEMS: [{ key: 'special:1' }],
}))

import { createPmCommandPaletteConfig } from '../../../src/command-palette/config'

describe('property-management command palette config', () => {
  beforeEach(() => {
    mocks.buildReportPageUrl.mockClear()
    mocks.getReportDefinitions.mockReset()
    mocks.searchCommandPalette.mockClear()
  })

  it('exposes stable configuration, menu, heuristic, and remote hooks', async () => {
    const router = { currentRoute: { value: { fullPath: '/home' } } }
    const config = createPmCommandPaletteConfig(router as never)

    expect(config.router).toBe(router)
    expect(config.getMenuGroups?.()).toBe(mocks.groups)
    expect(config.recentStorageKey).toBe('ngb:pm:command-palette:recent')
    expect(config.favoriteItems).toEqual([{ key: 'favorite:1' }])
    expect(config.createItems).toEqual([{ key: 'create:1' }])
    expect(config.specialPageItems).toEqual([{ key: 'special:1' }])
    expect(config.buildHeuristicCurrentActions?.('/home')).toEqual([{ key: 'heuristic:/home' }])
    await config.searchRemote?.({ query: 'lease', scope: 'all' } as never)
    expect(mocks.searchCommandPalette).toHaveBeenCalledWith({ query: 'lease', scope: 'all' })
  })

  it('filters diagnostics and maps report metadata, fallbacks, and rank', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'pm.aging', name: 'Aging', group: 'Receivables', description: 'Open balances' },
      { reportCode: 'accounting.posting_log', name: 'Posting', group: 'Diagnostics', description: 'hidden' },
      { reportCode: 'accounting.consistency', name: 'Consistency', group: 'Diagnostics', description: 'hidden' },
      { reportCode: 'pm.empty', name: 'Empty', group: null, description: null },
    ])
    const items = await createPmCommandPaletteConfig({} as never).loadReportItems?.()

    expect(items).toEqual([
      expect.objectContaining({
        key: 'report:pm.aging', subtitle: 'Receivables · Open balances', icon: 'icon:pm.aging',
        route: '/reports/pm.aging', keywords: ['pm.aging', 'Receivables', 'Open balances'], defaultRank: 700,
      }),
      expect.objectContaining({
        key: 'report:pm.empty', subtitle: 'Run this report', icon: 'icon:pm.empty',
        route: '/reports/pm.empty', keywords: ['pm.empty', '', ''], defaultRank: 699,
      }),
    ])
  })
})
