import { describe, expect, it } from 'vitest'

import {
  currentMonthValue,
  dateOnlyToMonthValue,
  formatMonthValue,
  monthValueToDateOnly,
  monthValueYear,
  normalizeDateOnlyValue,
  normalizeMonthValue,
  parseDateOnlyValue,
  parseMonthValue,
  relativeMonthValue,
  shiftMonthValue,
  toDateOnlyValue,
  toMonthValue,
} from '../../../../src/ngb/utils/dateValues'

describe('date values helpers', () => {
  it('parses and normalizes month values across year boundaries', () => {
    expect(parseMonthValue('2026-04')).toEqual({ year: 2026, month: 4 })
    expect(parseMonthValue('2026-13')).toBeNull()
    expect(normalizeMonthValue(' 2026-04 ')).toBe('2026-04')
    expect(toMonthValue(2026, 4)).toBe('2026-04')
    expect(currentMonthValue(new Date(2026, 3, 8))).toBe('2026-04')
    expect(monthValueYear('2026-04')).toBe(2026)
    expect(shiftMonthValue('2026-01', -1)).toBe('2025-12')
    expect(shiftMonthValue('2026-12', 1)).toBe('2027-01')
    expect(relativeMonthValue(2, new Date(2026, 10, 1))).toBe('2027-01')
  })

  it('converts between month/date-only values and rejects invalid calendar dates', () => {
    expect(monthValueToDateOnly('2026-04')).toBe('2026-04-01')
    expect(dateOnlyToMonthValue('2026-04-30')).toBe('2026-04')
    expect(normalizeDateOnlyValue('2026-02-29')).toBeNull()
    expect(normalizeDateOnlyValue('2024-02-29')).toBe('2024-02-29')

    const parsed = parseDateOnlyValue('2026-04-08')
    expect(parsed?.getFullYear()).toBe(2026)
    expect(parsed?.getMonth()).toBe(3)
    expect(parsed?.getDate()).toBe(8)
    expect(toDateOnlyValue(new Date(2026, 3, 8))).toBe('2026-04-08')

    expect(formatMonthValue('2026-04')).toContain('2026')
    expect(formatMonthValue('bad')).toBeNull()
  })
})
