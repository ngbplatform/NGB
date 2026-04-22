import { watch, type ComputedRef, type Ref } from 'vue'

import type { OpenItemsTabKey } from './presentation'

type SyncAfterContextLoadArgs = {
  contextChanged: boolean
  preferredTab: OpenItemsTabKey | null
  autoOpenApply: boolean
  clearAutoOpenApplyInRoute: () => void
  currentError: string | null
}

type UseOpenItemsRouteContextArgs<TTuple extends readonly unknown[]> = {
  source: () => TTuple
  contextKeyCount: number
  hydrateContext: () => Promise<void>
  load: () => Promise<void>
  preferredTab: ComputedRef<OpenItemsTabKey | null>
  currentError: Ref<string | null>
  syncAfterContextLoad: (args: SyncAfterContextLoadArgs) => Promise<void>
  autoOpenApply: (current: TTuple, previous: TTuple | undefined) => boolean
  clearAutoOpenApplyInRoute: (current: TTuple, previous: TTuple | undefined) => void
  shouldSkip?: (current: TTuple, previous: TTuple | undefined) => boolean
  afterSync?: (current: TTuple, previous: TTuple | undefined) => void | Promise<void>
}

function didContextChange(current: readonly unknown[], previous: readonly unknown[] | undefined, count: number): boolean {
  const baseline = previous ?? []
  for (let index = 0; index < count; index += 1) {
    if (current[index] !== baseline[index]) return true
  }
  return false
}

export function useOpenItemsRouteContext<TTuple extends readonly unknown[]>(args: UseOpenItemsRouteContextArgs<TTuple>) {
  watch(
    args.source,
    async (current, previous) => {
      if (args.shouldSkip?.(current, previous)) return

      await args.hydrateContext()
      await args.load()
      await args.syncAfterContextLoad({
        contextChanged: didContextChange(current, previous, args.contextKeyCount),
        preferredTab: args.preferredTab.value,
        autoOpenApply: args.autoOpenApply(current, previous),
        clearAutoOpenApplyInRoute: () => args.clearAutoOpenApplyInRoute(current, previous),
        currentError: args.currentError.value,
      })
      await args.afterSync?.(current, previous)
    },
    { immediate: true },
  )
}
