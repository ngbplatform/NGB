import { describe, expect, it } from 'vitest'

import { formatOccurredAtUtcValue } from '../../../../src/ngb/editor/documentEffectsDateFormatting'

describe('formatOccurredAtUtcValue', () => {
  it('formats UTC-midnight timestamps as UTC date-only to avoid previous-day local shifts', () => {
    expect(formatOccurredAtUtcValue('2026-04-07T00:00:00.000Z', 'en-US')).toBe('4/7/2026')
  })

  it('keeps non-midnight timestamps as date-time values', () => {
    const value = '2026-04-07T13:45:00.000Z'
    expect(formatOccurredAtUtcValue(value, 'en-US')).toBe(new Date(value).toLocaleString('en-US'))
  })

  it('returns invalid values unchanged', () => {
    expect(formatOccurredAtUtcValue('not-a-date', 'en-US')).toBe('not-a-date')
  })
})
