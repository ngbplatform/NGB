import { describe, expect, it } from 'vitest'

import {
  DEFAULT_ACCOUNTING_PERIOD_CLOSING_BASE_PATH,
  DEFAULT_CHART_OF_ACCOUNTS_BASE_PATH,
  DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH,
  buildAccountingPeriodClosingPath,
  buildChartOfAccountsPath,
  buildGeneralJournalEntriesListPath,
  buildGeneralJournalEntriesPath,
  isGeneralJournalEntryDocumentType,
} from '../../../../src/ngb/accounting/navigation'

describe('accounting navigation', () => {
  it('builds chart-of-accounts and period-closing paths with normalized query params', () => {
    expect(DEFAULT_CHART_OF_ACCOUNTS_BASE_PATH).toBe('/admin/chart-of-accounts')
    expect(buildChartOfAccountsPath({ panel: 'edit', id: 'acc-1' })).toBe('/admin/chart-of-accounts?panel=edit&id=acc-1')
    expect(buildChartOfAccountsPath({ basePath: '  /custom/accounts  ' })).toBe('/custom/accounts')

    expect(DEFAULT_ACCOUNTING_PERIOD_CLOSING_BASE_PATH).toBe('/admin/accounting/period-closing')
    expect(buildAccountingPeriodClosingPath({
      year: 2026,
      month: '2026-04',
      fy: '2026-12',
    })).toBe('/admin/accounting/period-closing?year=2026&month=2026-04&fy=2026-12')
  })

  it('builds journal entry paths and recognizes supported document types', () => {
    expect(DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH).toBe('/accounting/general-journal-entries')
    expect(buildGeneralJournalEntriesListPath()).toBe('/accounting/general-journal-entries')
    expect(buildGeneralJournalEntriesListPath({ basePath: '  /custom/journal  ' })).toBe('/custom/journal')
    expect(buildGeneralJournalEntriesPath()).toBe('/accounting/general-journal-entries/new')
    expect(buildGeneralJournalEntriesPath('doc/1')).toBe('/accounting/general-journal-entries/doc%2F1')

    expect(isGeneralJournalEntryDocumentType('accounting.general_journal_entry')).toBe(true)
    expect(isGeneralJournalEntryDocumentType(' general_journal_entry ')).toBe(true)
    expect(isGeneralJournalEntryDocumentType('pm.invoice')).toBe(false)
  })
})
