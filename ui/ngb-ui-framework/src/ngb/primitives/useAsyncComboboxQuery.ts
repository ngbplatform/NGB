import { computed, onBeforeUnmount, ref, watch, type ComputedRef } from 'vue'

type UseAsyncComboboxQueryArgs<TItem> = {
  disabled: ComputedRef<boolean>
  items: ComputedRef<readonly TItem[]>
  emitQuery: (query: string) => void
  debounceMs?: number
}

type ResetOptions = {
  emitEmptyQuery?: boolean
}

export function useAsyncComboboxQuery<TItem>(args: UseAsyncComboboxQueryArgs<TItem>) {
  const query = ref('')
  const pendingEmit = ref(false)
  const pendingResults = ref(false)
  const lastSentQuery = ref('')
  const debounceMs = args.debounceMs ?? 220

  let timer: ReturnType<typeof setTimeout> | null = null

  const isSearching = computed(() => pendingEmit.value || pendingResults.value)

  function clearTimer(): void {
    if (!timer) return
    clearTimeout(timer)
    timer = null
  }

  function clearPendingState(): void {
    pendingEmit.value = false
    pendingResults.value = false
    lastSentQuery.value = ''
  }

  function resetQueryState(options: ResetOptions = {}): void {
    query.value = ''
    clearTimer()
    clearPendingState()
    if (options.emitEmptyQuery) args.emitQuery('')
  }

  function onInput(value: string): void {
    if (args.disabled.value) return

    query.value = value
    const trimmed = value.trim()

    if (!trimmed) {
      resetQueryState({ emitEmptyQuery: true })
      return
    }

    pendingEmit.value = true
    pendingResults.value = false

    clearTimer()
    timer = setTimeout(() => {
      pendingEmit.value = false
      pendingResults.value = true
      lastSentQuery.value = query.value
      args.emitQuery(query.value)
    }, debounceMs)
  }

  watch(
    () => args.items.value,
    () => {
      if (pendingResults.value && query.value === lastSentQuery.value) {
        pendingResults.value = false
      }
    },
  )

  onBeforeUnmount(() => clearTimer())

  return {
    query,
    isSearching,
    clearPendingState,
    resetQueryState,
    onInput,
  }
}
