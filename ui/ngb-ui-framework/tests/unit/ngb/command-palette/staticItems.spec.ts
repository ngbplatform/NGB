import { describe, expect, it } from 'vitest'

import {
  buildNgbHeuristicCurrentActions,
  NGB_ACCOUNTING_CREATE_ITEMS,
  NGB_ACCOUNTING_FAVORITE_ITEMS,
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
} from '../../../../src/ngb/command-palette/staticItems'

describe('command palette static items', () => {
  it('builds contextual document and catalog actions from the current route', () => {
    expect(buildNgbHeuristicCurrentActions('/documents/pm.invoice/abc-123')).toEqual([
      expect.objectContaining({
        title: 'Open document flow',
        route: '/documents/pm.invoice/abc-123/flow',
        badge: 'Flow',
      }),
      expect.objectContaining({
        title: 'Open accounting effects',
        route: '/documents/pm.invoice/abc-123/effects',
        badge: 'Effects',
      }),
    ])

    expect(buildNgbHeuristicCurrentActions('/documents/pm.invoice/abc-123/effects')).toEqual([
      expect.objectContaining({
        title: 'Open source document',
        route: '/documents/pm.invoice/abc-123',
        badge: 'Document',
      }),
    ])

    expect(buildNgbHeuristicCurrentActions('/documents/pm.invoice')).toEqual([
      expect.objectContaining({
        title: 'Create new',
        route: '/documents/pm.invoice/new',
        badge: 'Create',
      }),
    ])

    expect(buildNgbHeuristicCurrentActions('/catalogs/pm.property')).toEqual([
      expect.objectContaining({
        title: 'Create new',
        route: '/catalogs/pm.property/new',
        badge: 'Create',
      }),
    ])
  })

  it('respects catalog exclusions and adds current create action on the journal list page', () => {
    expect(buildNgbHeuristicCurrentActions('/catalogs/pm.property', {
      excludedCatalogTypes: ['pm.property'],
    })).toEqual([])

    expect(buildNgbHeuristicCurrentActions('/accounting/general-journal-entries')).toEqual([
      expect.objectContaining({
        title: 'Create Journal Entry',
        route: '/accounting/general-journal-entries/new',
        badge: 'Create',
      }),
    ])
  })

  it('exposes the default favorite, create, and special accounting items', () => {
    expect(NGB_ACCOUNTING_FAVORITE_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({
        key: 'favorite:trial-balance',
        route: '/reports/accounting.trial_balance',
        kind: 'report',
      }),
      expect.objectContaining({
        key: 'favorite:period-closing',
        route: '/admin/accounting/period-closing',
        kind: 'page',
      }),
    ]))

    expect(NGB_ACCOUNTING_CREATE_ITEMS).toEqual([
      expect.objectContaining({
        key: 'create:gje',
        route: '/accounting/general-journal-entries/new',
        badge: 'Create',
      }),
    ])

    expect(NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS).toEqual(expect.arrayContaining([
      expect.objectContaining({
        key: 'page:chart-of-accounts',
        route: '/admin/chart-of-accounts',
      }),
      expect.objectContaining({
        key: 'page:posting-log',
        route: '/reports/accounting.posting_log',
      }),
    ]))
  })
})
