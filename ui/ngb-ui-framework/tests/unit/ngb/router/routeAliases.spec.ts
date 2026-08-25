import { describe, expect, it } from 'vitest'

import { buildGeneralJournalEntriesListPath, buildGeneralJournalEntriesPath } from '../../../../src/ngb/accounting/navigation'
import { buildReportPageUrl } from '../../../../src/ngb/reporting/navigation'
import type { RouteLocationRaw } from 'vue-router'

import {
  ngbRouteAliasRedirectRoutes,
  normalizeNgbRouteAliasPath,
} from '../../../../src/ngb/router/routeAliases'

function redirect(path: string, params: Record<string, unknown> = {}): RouteLocationRaw {
  const route = ngbRouteAliasRedirectRoutes.find((entry) => entry.path === path)
  expect(route).toBeDefined()
  expect(route?.redirect).toBeTypeOf('function')

  return (route?.redirect as (to: unknown) => RouteLocationRaw)({
    params,
    query: { back: 'encoded-back', tab: 'audit' },
    hash: '#details',
  })
}

describe('route alias normalization', () => {
  it('normalizes both legacy general journal entry prefixes into the modern accounting routes', () => {
    expect(normalizeNgbRouteAliasPath('/documents/general_journal_entry')).toBe(buildGeneralJournalEntriesListPath())
    expect(normalizeNgbRouteAliasPath('/documents/general_journal_entry/new')).toBe(buildGeneralJournalEntriesPath())
    expect(normalizeNgbRouteAliasPath('/documents/general_journal_entry/abc-123')).toBe('/accounting/general-journal-entries/abc-123')

    expect(normalizeNgbRouteAliasPath('/documents/accounting.general_journal_entry')).toBe(buildGeneralJournalEntriesListPath())
    expect(normalizeNgbRouteAliasPath('/documents/accounting.general_journal_entry/new')).toBe(buildGeneralJournalEntriesPath())
    expect(normalizeNgbRouteAliasPath('/documents/accounting.general_journal_entry/abc-123')).toBe('/accounting/general-journal-entries/abc-123')
  })

  it('normalizes legacy report aliases into their canonical report pages', () => {
    expect(normalizeNgbRouteAliasPath('/admin/accounting/posting-log')).toBe(buildReportPageUrl('accounting.posting_log'))
    expect(normalizeNgbRouteAliasPath('/admin/accounting/consistency')).toBe(buildReportPageUrl('accounting.consistency'))
  })

  it('leaves unrelated application routes unchanged', () => {
    expect(normalizeNgbRouteAliasPath('/receivables/open-items')).toBe('/receivables/open-items')
    expect(normalizeNgbRouteAliasPath('')).toBe('')
    expect(normalizeNgbRouteAliasPath(null)).toBe('')
    expect(normalizeNgbRouteAliasPath(undefined)).toBe('')
  })

  it('redirects every legacy journal route and preserves query plus hash state', () => {
    const context = {
      query: { back: 'encoded-back', tab: 'audit' },
      hash: '#details',
    }

    expect(redirect('/documents/general_journal_entry')).toEqual({
      path: '/accounting/general-journal-entries',
      ...context,
    })
    expect(redirect('/documents/general_journal_entry/new')).toEqual({
      path: '/accounting/general-journal-entries/new',
      ...context,
    })
    expect(redirect('/documents/general_journal_entry/:id', { id: 'entry / 1' })).toEqual({
      path: '/accounting/general-journal-entries/entry%20%2F%201',
      ...context,
    })
    expect(redirect('/documents/general_journal_entry/:id')).toEqual({
      path: '/accounting/general-journal-entries/new',
      ...context,
    })
    expect(redirect('/documents/accounting.general_journal_entry')).toEqual({
      path: '/accounting/general-journal-entries',
      ...context,
    })
    expect(redirect('/documents/accounting.general_journal_entry/new')).toEqual({
      path: '/accounting/general-journal-entries/new',
      ...context,
    })
    expect(redirect('/documents/accounting.general_journal_entry/:id')).toEqual({
      path: '/accounting/general-journal-entries/new',
      ...context,
    })
  })

  it('redirects both legacy report routes while preserving context', () => {
    const context = {
      query: { back: 'encoded-back', tab: 'audit' },
      hash: '#details',
    }

    expect(redirect('/admin/accounting/posting-log')).toEqual({
      path: '/reports/accounting.posting_log',
      ...context,
    })
    expect(redirect('/admin/accounting/consistency')).toEqual({
      path: '/reports/accounting.consistency',
      ...context,
    })
  })
})
