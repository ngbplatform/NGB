import { describe, expect, it } from 'vitest'

import {
  normalizeEntityEditorIdRouteParam,
  normalizeRequiredRouteParam,
  normalizeRouteParam,
} from '../../../../src/ngb/router/routeParams'

describe('route params helpers', () => {
  it('normalizes scalar and array route params into trimmed strings', () => {
    expect(normalizeRouteParam('  pm.invoice  ')).toBe('pm.invoice')
    expect(normalizeRouteParam(['  abc-123  ', 'ignored'])).toBe('abc-123')
  })

  it('returns null or an empty string when the route param is missing', () => {
    expect(normalizeRouteParam(undefined)).toBeNull()
    expect(normalizeRouteParam('   ')).toBeNull()
    expect(normalizeRequiredRouteParam(undefined)).toBe('')
  })

  it('treats the full-page editor new sentinel as an undefined entity id', () => {
    expect(normalizeEntityEditorIdRouteParam(' doc-1 ')).toBe('doc-1')
    expect(normalizeEntityEditorIdRouteParam(' new ')).toBeUndefined()
    expect(normalizeEntityEditorIdRouteParam(['NEW'])).toBeUndefined()
    expect(normalizeEntityEditorIdRouteParam(undefined)).toBeUndefined()
  })
})
