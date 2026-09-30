import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
  getReportDefinitions: vi.fn(),
  groups: [{ title: 'CRM' }],
  search: vi.fn(async () => [{ key: 'remote:1' }]),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildReportPageUrl: mocks.buildReportPageUrl,
  getReportDefinitions: mocks.getReportDefinitions,
  searchCommandPalette: mocks.search,
  useMainMenuStore: () => ({ groups: mocks.groups }),
}))
vi.mock('../../../src/command-palette/crmStaticItems', () => ({
  buildCRMHeuristicCurrentActions: vi.fn((path: string) => [{ key: `heuristic:${path}` }]),
  resolveCRMReportPaletteIcon: vi.fn(({ reportCode }: { reportCode?: string }) => `icon:${reportCode}`),
  CRM_CREATE_COMMAND_ITEMS: [{ key: 'create' }],
  CRM_FAVORITE_ITEMS: [{ key: 'favorite' }],
  CRM_SPECIAL_PAGE_ITEMS: [{ key: 'special' }],
}))

import { createCRMCommandPaletteConfig } from '../../../src/command-palette/config'

describe('CRM command palette config', () => {
  beforeEach(() => {
    mocks.buildReportPageUrl.mockClear()
    mocks.getReportDefinitions.mockReset()
    mocks.search.mockClear()
  })

  it('wires menu, heuristic, static, and remote-search hooks', async () => {
    const router = {}
    const config = createCRMCommandPaletteConfig(router as never)
    expect(config.router).toBe(router)
    expect(config.getMenuGroups?.()).toBe(mocks.groups)
    expect(config.recentStorageKey).toBe('ngb:crm:command-palette:recent')
    expect(config.buildHeuristicCurrentActions?.('/home')).toEqual([{ key: 'heuristic:/home' }])
    expect(config.favoriteItems).toEqual([{ key: 'favorite' }])
    expect(config.createItems).toEqual([{ key: 'create' }])
    expect(config.specialPageItems).toEqual([{ key: 'special' }])
    await config.searchRemote?.({ query: 'account' } as never)
    expect(mocks.search).toHaveBeenCalledWith({ query: 'account' })
  })

  it('keeps CRM reports and maps metadata and subtitle fallbacks', async () => {
    mocks.getReportDefinitions.mockResolvedValue([
      { reportCode: 'crm.pipeline', name: 'Pipeline', group: 'Sales', description: 'Open deals' },
      { reportCode: 'trade.sales', name: 'Trade', group: 'Trade', description: 'Hidden' },
      { reportCode: 'crm.empty', name: 'Empty', group: null, description: null },
    ])
    const items = await createCRMCommandPaletteConfig({} as never).loadReportItems?.()
    expect(items).toEqual([
      expect.objectContaining({
        key: 'report:crm.pipeline', subtitle: 'Sales · Open deals', icon: 'icon:crm.pipeline',
        route: '/reports/crm.pipeline', keywords: ['crm.pipeline', 'Sales', 'Open deals'], defaultRank: 700,
      }),
      expect.objectContaining({
        key: 'report:crm.empty', subtitle: 'Run this report', keywords: ['crm.empty', '', ''], defaultRank: 699,
      }),
    ])
  })
})
