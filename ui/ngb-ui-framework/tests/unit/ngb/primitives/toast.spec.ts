import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const vueMocks = vi.hoisted(() => ({
  inject: vi.fn(),
  provide: vi.fn(),
  reactive: vi.fn(<T>(value: T) => value),
}))

vi.mock('vue', () => ({
  inject: vueMocks.inject,
  provide: vueMocks.provide,
  reactive: vueMocks.reactive,
}))

import { provideToasts, useToasts } from '../../../../src/ngb/primitives/toast'

describe('toast api helpers', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vueMocks.inject.mockReset()
    vueMocks.provide.mockReset()
    vueMocks.reactive.mockClear()
    vi.stubGlobal('window', { setTimeout })
    vi.stubGlobal('crypto', {
      randomUUID: vi.fn()
        .mockReturnValueOnce('toast-1')
        .mockReturnValueOnce('toast-2')
        .mockReturnValueOnce('toast-3')
        .mockReturnValueOnce('toast-4')
        .mockReturnValueOnce('toast-5')
        .mockReturnValueOnce('toast-6'),
    })
  })

  afterEach(() => {
    vi.runOnlyPendingTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('provides a bounded toast queue with defaults and timed removal', () => {
    const api = provideToasts()

    api.push({ title: 'First' })
    api.push({ title: 'Second', tone: 'success', timeoutMs: 0 })
    api.push({ title: 'Third', timeoutMs: 0 })
    api.push({ title: 'Fourth', timeoutMs: 0 })
    api.push({ title: 'Fifth', timeoutMs: 0 })
    api.push({ title: 'Sixth', timeoutMs: 0 })

    expect(vueMocks.provide).toHaveBeenCalledTimes(1)
    expect(api.toasts.map((toast) => toast.id)).toEqual([
      'toast-6',
      'toast-5',
      'toast-4',
      'toast-3',
      'toast-2',
    ])
    expect(api.toasts.find((toast) => toast.id === 'toast-2')).toMatchObject({
      tone: 'success',
      timeoutMs: 0,
    })

    vi.advanceTimersByTime(3500)
    expect(api.toasts.map((toast) => toast.id)).toEqual([
      'toast-6',
      'toast-5',
      'toast-4',
      'toast-3',
      'toast-2',
    ])

    api.remove('toast-2')
    expect(api.toasts.map((toast) => toast.id)).not.toContain('toast-2')
  })

  it('returns the injected toast api and throws when no provider exists', () => {
    const api = {
      toasts: [],
      push: vi.fn(),
      remove: vi.fn(),
    }

    vueMocks.inject.mockReturnValueOnce(api)
    expect(useToasts()).toBe(api)

    vueMocks.inject.mockReturnValueOnce(undefined)
    expect(() => useToasts()).toThrow('useToasts(): missing provideToasts()')
  })
})
