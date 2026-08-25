import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildDocumentFullPageUrl: vi.fn((documentType: string) => `/documents/${documentType}/new`),
  buildNgbHeuristicCurrentActions: vi.fn((fullRoute: string) => [{ key: `heuristic:${fullRoute}` }]),
  buildReportPageUrl: vi.fn((reportCode: string) => `/reports/${reportCode}`),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildDocumentFullPageUrl: mocks.buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions: mocks.buildNgbHeuristicCurrentActions,
  buildReportPageUrl: mocks.buildReportPageUrl,
  NGB_ACCOUNTING_CREATE_ITEMS: [{ key: 'accounting:create', route: '/accounting/create' }],
  NGB_ACCOUNTING_FAVORITE_ITEMS: [{ key: 'accounting:favorite', route: '/accounting/favorite' }],
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS: [{ key: 'accounting:special', route: '/accounting/special' }],
}))

import {
  buildPmHeuristicCurrentActions,
  PM_CREATE_COMMAND_ITEMS,
  PM_FAVORITE_ITEMS,
  PM_SPECIAL_PAGE_ITEMS,
  resolvePmReportPaletteIcon,
} from '../../../src/command-palette/pmStaticItems'

describe('property-management static command palette items', () => {
  beforeEach(() => {
    mocks.buildDocumentFullPageUrl.mockClear()
    mocks.buildNgbHeuristicCurrentActions.mockClear()
  })

  it('delegates heuristics and adds the property-list building action only in its exact context', () => {
    expect(buildPmHeuristicCurrentActions('')).toEqual([{ key: 'heuristic:' }])
    expect(buildPmHeuristicCurrentActions('/catalogs/pm.property?trash=active')).toEqual([
      { key: 'heuristic:/catalogs/pm.property?trash=active' },
      expect.objectContaining({
        key: 'current:create-building', route: '/catalogs/pm.property?panel=new&newKind=Building',
        subtitle: 'Start a new building record', icon: 'plus', badge: 'Create', hint: null,
        commandCode: null, status: null, openInNewTabSupported: true, defaultRank: 980, isCurrentContext: true,
      }),
    ])
    expect(buildPmHeuristicCurrentActions('/catalogs/pm.property/1')).toEqual([
      { key: 'heuristic:/catalogs/pm.property/1' },
    ])
    expect(mocks.buildNgbHeuristicCurrentActions).toHaveBeenCalledWith('', {
      excludedCatalogTypes: ['pm.accounting_policy', 'pm.property'],
    })
  })

  it.each([
    ['pm.tenant.statement', 'file-text'], ['pm.receivables.open_items.details', 'file-text'],
    ['pm.maintenance.queue', 'list'], ['pm.receivables.open_items', 'list'],
    ['accounting.general_journal', 'receipt'], ['accounting.account_card', 'book-open'],
    ['accounting.general_ledger_aggregated', 'book-open'], ['unknown', 'bar-chart'], [null, 'bar-chart'],
  ])('resolves report icon for %s', (reportCode, expected) => {
    expect(resolvePmReportPaletteIcon({ reportCode })).toBe(expected)
  })

  it('publishes PM create commands, favorites, and special pages with platform accounting entries', () => {
    expect(PM_CREATE_COMMAND_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'create:lease', route: '/documents/pm.lease/new', keywords: ['create', 'new', 'lease'] }),
      expect.objectContaining({ key: 'create:payable-credit-memo', commandCode: 'create:payable-credit-memo' }),
    ]))
    expect(PM_CREATE_COMMAND_ITEMS.at(-1)).toEqual({ key: 'accounting:create', route: '/accounting/create' })
    expect(PM_FAVORITE_ITEMS[0]).toMatchObject({ key: 'favorite:receivables-open-items', route: '/receivables/open-items' })
    expect(PM_FAVORITE_ITEMS).toContainEqual({ key: 'accounting:favorite', route: '/accounting/favorite' })
    expect(PM_FAVORITE_ITEMS.at(-1)).toMatchObject({ key: 'favorite:maintenance-queue', route: '/reports/pm.maintenance.queue' })
    expect(PM_SPECIAL_PAGE_ITEMS).toEqual([
      { key: 'accounting:special', route: '/accounting/special' },
      expect.objectContaining({ key: 'page:accounting-policy', route: '/catalogs/pm.accounting_policy', subtitle: 'Setup & Controls' }),
    ])
  })
})
