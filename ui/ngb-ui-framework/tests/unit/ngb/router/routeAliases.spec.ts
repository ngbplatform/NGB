import { describe, expect, it } from 'vitest'

import { buildGeneralJournalEntriesListPath, buildGeneralJournalEntriesPath } from '../../../../src/ngb/accounting/navigation'
import { buildReportPageUrl } from '../../../../src/ngb/reporting/navigation'
import { normalizeNgbRouteAliasPath } from '../../../../src/ngb/router/routeAliases'

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
  })
})
