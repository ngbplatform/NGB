import { afterEach, describe, expect, it, vi } from 'vitest'

import { clonePlainData } from '../../../../src/ngb/utils/clone'

class CustomValue {
  constructor(readonly label: string) {}
}

describe('clonePlainData', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('uses structuredClone when available', () => {
    const structuredCloneMock = vi.fn((value: unknown) => ({ ...(value as Record<string, unknown>), cloned: true }))
    vi.stubGlobal('structuredClone', structuredCloneMock)

    expect(clonePlainData({ value: 42 })).toEqual({ value: 42, cloned: true })
    expect(structuredCloneMock).toHaveBeenCalledWith({ value: 42 })
  })

  it('falls back to recursive cloning for dto-like data when structuredClone is missing or throws', () => {
    vi.stubGlobal('structuredClone', vi.fn(() => {
      throw new Error('unsupported')
    }))

    const original = {
      createdAt: new Date('2026-04-08T12:00:00Z'),
      nested: {
        tags: ['open'],
      },
      custom: new CustomValue('keep-reference'),
    }

    const cloned = clonePlainData(original)

    expect(cloned).not.toBe(original)
    expect(cloned.createdAt).not.toBe(original.createdAt)
    expect(cloned.createdAt.toISOString()).toBe(original.createdAt.toISOString())
    expect(cloned.nested).not.toBe(original.nested)
    expect(cloned.nested.tags).not.toBe(original.nested.tags)
    expect(cloned.custom).toBe(original.custom)
  })
})
