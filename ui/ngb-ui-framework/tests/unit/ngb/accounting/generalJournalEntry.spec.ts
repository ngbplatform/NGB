import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  createGeneralJournalEntryLine,
  formatGeneralJournalEntryMoney,
  generalJournalEntryApprovalStateLabel,
  generalJournalEntryJournalTypeLabel,
  generalJournalEntrySourceLabel,
  normalizeDateOnly,
  normalizeGeneralJournalEntryApprovalState,
  normalizeGeneralJournalEntrySource,
  parseGeneralJournalEntryAmount,
  todayDateOnly,
  toUtcMidday,
} from '../../../../src/ngb/accounting/generalJournalEntry'

describe('generalJournalEntry helpers', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('creates a blank editable line model', () => {
    expect(createGeneralJournalEntryLine('line-1')).toEqual({
      clientKey: 'line-1',
      side: 1,
      account: null,
      amount: '',
      memo: '',
      dimensions: {},
    })
  })

  it('parses amount strings and formats money consistently', () => {
    expect(parseGeneralJournalEntryAmount('1,250.50')).toBe(1250.5)
    expect(Number.isNaN(parseGeneralJournalEntryAmount('oops'))).toBe(true)
    expect(formatGeneralJournalEntryMoney(1250.5)).toBe('1,250.50')
  })

  it('normalizes date-only values and converts them to utc midday timestamps', () => {
    expect(normalizeDateOnly('2026-04-08T22:15:00Z')).toBe('2026-04-08')
    expect(normalizeDateOnly('')).toBeNull()
    expect(toUtcMidday('2026-04-08')).toBe('2026-04-08T12:00:00Z')
    expect(() => toUtcMidday(null)).toThrow('Date is required.')
  })

  it('derives todayDateOnly from the current clock', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-08T09:30:00Z'))

    expect(todayDateOnly()).toBe('2026-04-08')
  })

  it('maps approval, source, and journal type labels from mixed inputs', () => {
    expect(normalizeGeneralJournalEntryApprovalState('submitted')).toBe(2)
    expect(generalJournalEntryApprovalStateLabel(3)).toBe('Approved')
    expect(normalizeGeneralJournalEntrySource('system')).toBe(2)
    expect(generalJournalEntrySourceLabel(1)).toBe('Manual')
    expect(generalJournalEntryJournalTypeLabel(5)).toBe('Closing')
    expect(generalJournalEntryJournalTypeLabel('unknown')).toBe('—')
  })
})
