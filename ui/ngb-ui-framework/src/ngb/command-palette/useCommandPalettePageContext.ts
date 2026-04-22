import { getCurrentInstance, onBeforeUnmount, watchEffect } from 'vue'
import type { CommandPaletteExplicitContext } from './types'
import { useCommandPaletteStore } from './store'

export function useCommandPalettePageContext(resolve: () => CommandPaletteExplicitContext | null | undefined): void {
  const store = useCommandPaletteStore()
  const instance = getCurrentInstance()
  const ownerId = `command-palette:${instance?.uid ?? Math.random().toString(36).slice(2)}`

  watchEffect(() => {
    store.setExplicitContext(ownerId, resolve() ?? null)
  })

  onBeforeUnmount(() => {
    store.clearExplicitContext(ownerId)
  })
}

