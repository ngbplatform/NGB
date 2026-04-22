import { onBeforeUnmount, onMounted, type ComputedRef } from 'vue'
import { readStorageString, removeStorageItem, writeStorageString } from 'ngb-ui-framework'

type UseOpenItemsNavigationRefreshArgs = {
  enabled: ComputedRef<boolean>
  load: () => Promise<void>
  refreshFromRoute: ComputedRef<boolean>
  clearRefreshFlagInRoute: () => void
  sessionStorageKey?: string
}

export function useOpenItemsNavigationRefresh(args: UseOpenItemsNavigationRefreshArgs) {
  function markNeedsRefresh(): void {
    if (!args.sessionStorageKey) return
    void writeStorageString('session', args.sessionStorageKey, '1')
  }

  function consumeRefreshFlag(): boolean {
    if (!args.sessionStorageKey) return false
    if (readStorageString('session', args.sessionStorageKey) !== '1') return false
    removeStorageItem('session', args.sessionStorageKey)
    return true
  }

  async function refreshIfNeededFromNavigation(): Promise<void> {
    const shouldRefresh = args.refreshFromRoute.value || consumeRefreshFlag()
    if (!shouldRefresh || !args.enabled.value) return

    await args.load()
    if (args.refreshFromRoute.value) args.clearRefreshFlagInRoute()
  }

  function handleWindowFocus(): void {
    void refreshIfNeededFromNavigation()
  }

  function handleVisibilityChange(): void {
    if (typeof document !== 'undefined' && document.visibilityState === 'visible') {
      void refreshIfNeededFromNavigation()
    }
  }

  onMounted(async () => {
    if (typeof window === 'undefined' || typeof document === 'undefined') return

    window.addEventListener('focus', handleWindowFocus)
    document.addEventListener('visibilitychange', handleVisibilityChange)

    if (!args.refreshFromRoute.value) await refreshIfNeededFromNavigation()
  })

  onBeforeUnmount(() => {
    if (typeof window === 'undefined' || typeof document === 'undefined') return

    window.removeEventListener('focus', handleWindowFocus)
    document.removeEventListener('visibilitychange', handleVisibilityChange)
  })

  return {
    markNeedsRefresh,
    refreshIfNeededFromNavigation,
  }
}
