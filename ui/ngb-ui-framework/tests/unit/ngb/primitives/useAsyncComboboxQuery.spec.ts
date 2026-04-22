import { computed, nextTick, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const beforeUnmountCallbacks = vi.hoisted(() => [] as Array<() => void>)

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    onBeforeUnmount: (callback: () => void) => {
      beforeUnmountCallbacks.push(callback)
    },
  }
})

import { useAsyncComboboxQuery } from '../../../../../src/ngb/primitives/useAsyncComboboxQuery'

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

function createHarness() {
  const disabled = ref(false)
  const items = ref<Array<{ id: string }>>([])
  const emitted: string[] = []

  const state = useAsyncComboboxQuery({
    disabled: computed(() => disabled.value),
    items: computed(() => items.value),
    emitQuery: (query) => {
      emitted.push(query)
    },
    debounceMs: 50,
  })

  return {
    disabled,
    emitted,
    items,
    state,
  }
}

describe('useAsyncComboboxQuery', () => {
  beforeEach(() => {
    beforeUnmountCallbacks.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('debounces query emission and clears pending results when items arrive', async () => {
    const { emitted, items, state } = createHarness()

    state.onInput('cash')

    expect(state.query.value).toBe('cash')
    expect(state.isSearching.value).toBe(true)
    expect(emitted).toEqual([])

    vi.advanceTimersByTime(49)
    expect(emitted).toEqual([])

    vi.advanceTimersByTime(1)
    expect(emitted).toEqual(['cash'])
    expect(state.isSearching.value).toBe(true)

    items.value = [{ id: 'cash-id' }]
    await nextTick()

    expect(state.isSearching.value).toBe(false)
  })

  it('resets state and emits an empty query when the input is cleared', () => {
    const { emitted, state } = createHarness()

    state.onInput('cash')
    vi.advanceTimersByTime(50)
    expect(emitted).toEqual(['cash'])

    state.onInput('   ')

    expect(state.query.value).toBe('')
    expect(state.isSearching.value).toBe(false)
    expect(emitted).toEqual(['cash', ''])
  })

  it('ignores input while disabled and cancels pending timers on unmount', () => {
    const { disabled, emitted, state } = createHarness()

    disabled.value = true
    state.onInput('blocked')
    vi.advanceTimersByTime(100)
    expect(state.query.value).toBe('')
    expect(emitted).toEqual([])

    disabled.value = false
    state.onInput('queued')
    runBeforeUnmountHooks()
    vi.advanceTimersByTime(100)

    expect(emitted).toEqual([])
  })
})
