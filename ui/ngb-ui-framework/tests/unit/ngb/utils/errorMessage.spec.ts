import { describe, expect, it } from 'vitest'

import { toErrorMessage } from '../../../../src/ngb/utils/errorMessage'

describe('toErrorMessage', () => {
  it('prefers Error instances and message-like object payloads', () => {
    expect(toErrorMessage(new Error('Explicit failure'))).toBe('Explicit failure')
    expect(toErrorMessage(new Error('   '), 'Fallback')).toBe('Error:')
    expect(toErrorMessage({ message: 'From envelope' })).toBe('From envelope')
    expect(toErrorMessage({ message: null }, 'Fallback')).toBe('[object Object]')
    expect(toErrorMessage({ message: '   ' }, 'Fallback')).toBe('[object Object]')
  })

  it('falls back to stringified values or the provided fallback text', () => {
    expect(toErrorMessage('Plain error')).toBe('Plain error')
    expect(toErrorMessage('', 'Fallback message')).toBe('Fallback message')
    expect(toErrorMessage(null, 'Fallback message')).toBe('Fallback message')
    expect(toErrorMessage({ detail: 'missing message' }, 'Fallback message')).toBe('[object Object]')
  })
})
