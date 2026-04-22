import { describe, expect, it } from 'vitest'

import { NGB_ICON_NAMES, coerceNgbIconName, isNgbIconName } from '../../../../src/ngb/primitives/iconNames'

describe('icon names', () => {
  it('publishes a stable icon catalog without duplicates', () => {
    expect(NGB_ICON_NAMES).toContain('search')
    expect(NGB_ICON_NAMES).toContain('document-flow')
    expect(new Set(NGB_ICON_NAMES).size).toBe(NGB_ICON_NAMES.length)
  })

  it('recognizes valid icon names and coerces invalid values to a fallback', () => {
    expect(isNgbIconName('arrow-left')).toBe(true)
    expect(isNgbIconName('missing-icon')).toBe(false)
    expect(isNgbIconName(null)).toBe(false)

    expect(coerceNgbIconName('plus', 'search')).toBe('plus')
    expect(coerceNgbIconName('missing-icon', 'search')).toBe('search')
    expect(coerceNgbIconName(undefined, 'search')).toBe('search')
  })
})
