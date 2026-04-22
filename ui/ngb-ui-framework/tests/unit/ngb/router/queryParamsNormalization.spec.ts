import { describe, expect, it } from 'vitest'

import {
  firstQueryValue,
  mergeCleanQuery,
  normalizeMonthQueryValue,
  normalizeMonthValue,
  normalizeSingleQueryValue,
  normalizeYearQueryValue,
} from '../../../../src/ngb/router/queryParams'

describe('query params normalization helpers', () => {
  it('normalizes single query values from scalars and arrays', () => {
    expect(normalizeSingleQueryValue('  open items  ')).toBe('open items')
    expect(normalizeSingleQueryValue(['  first  ', 'second'])).toBe('first')
    expect(normalizeSingleQueryValue(undefined)).toBe('')
  })

  it('returns the first non-empty query value when present', () => {
    expect(firstQueryValue('  doc-1  ')).toBe('doc-1')
    expect(firstQueryValue(['', 'ignored'])).toBeNull()
    expect(firstQueryValue(null)).toBeNull()
  })

  it('normalizes month and year query values into valid primitives', () => {
    expect(normalizeMonthValue(' 2026-04 ')).toBe('2026-04')
    expect(normalizeMonthValue('2026-13')).toBe('')
    expect(normalizeMonthQueryValue('2026-04')).toBe('2026-04')
    expect(normalizeMonthQueryValue('invalid')).toBeNull()

    expect(normalizeYearQueryValue('2026')).toBe(2026)
    expect(normalizeYearQueryValue('1899')).toBeNull()
    expect(normalizeYearQueryValue('abcd')).toBeNull()
  })

  it('merges base and patch queries while dropping empty values', () => {
    expect(mergeCleanQuery(
      {
        panel: 'edit',
        id: 'doc-1',
        stale: 'remove-me',
      },
      {
        panel: null,
        search: 'lease',
        stale: '',
        page: 0,
      },
    )).toEqual({
      id: 'doc-1',
      search: 'lease',
      page: 0,
    })
  })
})
