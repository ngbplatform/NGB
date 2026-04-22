import { beforeEach, describe, expect, it, vi } from 'vitest'

const beforeUnmountCallbacks = vi.hoisted(() => [] as Array<() => void>)
const watchEffectCallbacks = vi.hoisted(() => [] as Array<() => void>)
const store = vi.hoisted(() => ({
  setExplicitContext: vi.fn(),
  clearExplicitContext: vi.fn(),
}))

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    getCurrentInstance: () => ({ uid: 42 }),
    onBeforeUnmount: (callback: () => void) => {
      beforeUnmountCallbacks.push(callback)
    },
    watchEffect: (callback: () => void) => {
      watchEffectCallbacks.push(callback)
      callback()
      return () => undefined
    },
  }
})

vi.mock('../../../../src/ngb/command-palette/store', () => ({
  useCommandPaletteStore: () => store,
}))

import { useCommandPalettePageContext } from '../../../../src/ngb/command-palette/useCommandPalettePageContext'

describe('useCommandPalettePageContext', () => {
  beforeEach(() => {
    beforeUnmountCallbacks.length = 0
    watchEffectCallbacks.length = 0
    store.setExplicitContext.mockClear()
    store.clearExplicitContext.mockClear()
  })

  it('publishes the explicit page context and clears it on unmount', () => {
    let currentContext = {
      entityType: 'page' as const,
      title: 'Properties',
      actions: [],
    }

    useCommandPalettePageContext(() => currentContext)

    expect(store.setExplicitContext).toHaveBeenCalledWith('command-palette:42', currentContext)

    currentContext = {
      entityType: 'catalog',
      catalogType: 'pm.property',
      title: 'Property',
      actions: [],
    }
    watchEffectCallbacks[0]?.()

    expect(store.setExplicitContext).toHaveBeenLastCalledWith('command-palette:42', currentContext)

    beforeUnmountCallbacks[0]?.()
    expect(store.clearExplicitContext).toHaveBeenCalledWith('command-palette:42')
  })

  it('stores null when the resolver does not provide a context', () => {
    useCommandPalettePageContext(() => null)

    expect(store.setExplicitContext).toHaveBeenCalledWith('command-palette:42', null)
  })
})
