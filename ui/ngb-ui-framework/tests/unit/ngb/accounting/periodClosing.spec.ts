import { describe, expect, it } from 'vitest'

import {
  alignMonthValueToYear,
  currentCalendarYear,
  defaultMonthValueForYear,
  formatPeriodDateOnly,
  formatPeriodMonthValue,
  resolveSelectedMonthValue,
  resolveSelectedYear,
  selectMonthValue,
  toPeriodDateOnly,
} from '../../../../src/ngb/accounting/periodClosing'

describe('period closing helpers', () => {
  it('resolves selected years and months from explicit query values and current date fallbacks', () => {
    const now = new Date(2026, 3, 8)

    expect(currentCalendarYear(now)).toBe(2026)
    expect(defaultMonthValueForYear(2026, now)).toBe('2026-04')
    expect(defaultMonthValueForYear(2025, now)).toBe('2025-01')
    expect(alignMonthValueToYear('2024-11', 2026, now)).toBe('2026-11')
    expect(alignMonthValueToYear('bad', 2026, now)).toBe('2026-04')

    expect(resolveSelectedYear({ year: '2024' }, now)).toBe(2024)
    expect(resolveSelectedYear({ month: '2025-11' }, now)).toBe(2025)
    expect(resolveSelectedYear({ fy: '2023-12' }, now)).toBe(2023)
    expect(resolveSelectedYear({}, now)).toBe(2026)

    expect(resolveSelectedMonthValue('2026-03', 2026, now)).toBe('2026-03')
    expect(resolveSelectedMonthValue('2025-03', 2026, now)).toBe('2026-04')
  })

  it('normalizes date/month selection and formats display-safe period values', () => {
    expect(selectMonthValue('2026-04')).toBe('2026-04')
    expect(selectMonthValue('2026-04-01')).toBe('2026-04')
    expect(selectMonthValue('invalid')).toBeNull()

    expect(toPeriodDateOnly('2026-04')).toBe('2026-04-01')
    expect(formatPeriodMonthValue('2026-04')).toContain('2026')
    expect(formatPeriodMonthValue('invalid')).toBe('—')
    expect(formatPeriodDateOnly('2026-04-01')).toContain('2026')
    expect(formatPeriodDateOnly('invalid')).toBe('—')
  })
})
