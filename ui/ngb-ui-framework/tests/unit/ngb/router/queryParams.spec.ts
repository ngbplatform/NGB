import { describe, expect, it } from 'vitest'

import {
  cleanQueryObject,
  normalizeAllowedQueryValue,
  normalizeBooleanQueryFlag,
  normalizeDateOnlyQueryValue,
  normalizeTrashMode,
} from '../../../../src/ngb/router/queryParams'

describe('query params helpers', () => {
  it('normalizes supported values', () => {
    expect(normalizeTrashMode('deleted')).toBe('deleted')
    expect(normalizeTrashMode('all')).toBe('all')
    expect(normalizeTrashMode('unexpected')).toBe('active')

    expect(normalizeBooleanQueryFlag('true')).toBe(true)
    expect(normalizeBooleanQueryFlag('1')).toBe(true)
    expect(normalizeBooleanQueryFlag('no')).toBe(false)

    expect(normalizeAllowedQueryValue('Balance', ['Movement', 'Balance'] as const)).toBe('Balance')
    expect(normalizeAllowedQueryValue('Other', ['Movement', 'Balance'] as const)).toBeNull()

    expect(normalizeDateOnlyQueryValue('2026-04-07')).toBe('2026-04-07')
    expect(normalizeDateOnlyQueryValue('2026-02-31')).toBeNull()
  })

  it('removes empty query entries', () => {
    expect(cleanQueryObject({
      keep: 'value',
      empty: '',
      nullable: null,
      presentZero: 0,
    })).toEqual({
      keep: 'value',
      presentZero: 0,
    })
  })
})
