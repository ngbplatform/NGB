import { describe, expect, it } from 'vitest'

import { toErrorMessage } from '../../../../src/ngb/utils/errorMessage'

describe('toErrorMessage', () => {
  it('prefers Error instances and message-like object payloads', () => {
    expect(toErrorMessage(new Error('Explicit failure'))).toBe('Explicit failure')
    expect(toErrorMessage({ message: 'From envelope' })).toBe('From envelope')
  })

  it('falls back to stringified values or the provided fallback text', () => {
    expect(toErrorMessage('Plain error')).toBe('Plain error')
    expect(toErrorMessage('', 'Fallback message')).toBe('Fallback message')
    expect(toErrorMessage({ detail: 'missing message' }, 'Fallback message')).toBe('[object Object]')
  })
})
