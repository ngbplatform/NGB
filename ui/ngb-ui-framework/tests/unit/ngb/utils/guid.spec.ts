import { describe, expect, it } from 'vitest'

import { EMPTY_GUID, isEmptyGuid, isGuidString, isNonEmptyGuid, shortGuid } from '../../../../src/ngb/utils/guid'

describe('guid helpers', () => {
  it('recognizes valid and empty guid values', () => {
    expect(isGuidString('11111111-1111-1111-1111-111111111111')).toBe(true)
    expect(isGuidString('bad-guid')).toBe(false)
    expect(isEmptyGuid(EMPTY_GUID)).toBe(true)
    expect(isNonEmptyGuid('11111111-1111-1111-1111-111111111111')).toBe(true)
    expect(isNonEmptyGuid(EMPTY_GUID)).toBe(false)
  })

  it('shortens long guid strings and preserves short/non-string fallbacks', () => {
    expect(shortGuid('11111111-1111-1111-1111-111111111111')).toBe('11111111…1111')
    expect(shortGuid('short-id')).toBe('short-id')
    expect(shortGuid(null)).toBe('—')
  })
})
