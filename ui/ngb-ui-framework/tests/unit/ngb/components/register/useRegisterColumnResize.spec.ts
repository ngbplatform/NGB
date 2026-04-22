import { computed, ref } from 'vue'
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

import { useRegisterColumnResize } from '../../../../../src/ngb/components/register/useRegisterColumnResize'
import type { RegisterColumn } from '../../../../../src/ngb/components/register/registerTypes'

type ListenerEntry = {
  listener: (event: PointerEvent) => void
  signal?: AbortSignal
}

class FakeHTMLElement {
  listeners = new Map<string, ListenerEntry[]>()
  setPointerCapture = vi.fn()
  releasePointerCapture = vi.fn()

  addEventListener(type: string, listener: EventListenerOrEventListenerObject, options?: AddEventListenerOptions | boolean) {
    const next: ListenerEntry = {
      listener: listener as (event: PointerEvent) => void,
      signal: typeof options === 'object' && options ? options.signal : undefined,
    }

    const list = this.listeners.get(type) ?? []
    list.push(next)
    this.listeners.set(type, list)
  }

  dispatch(type: string, event: PointerEvent) {
    for (const entry of this.listeners.get(type) ?? []) {
      if (entry.signal?.aborted) continue
      entry.listener(event)
    }
  }
}

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

function pointerEvent(args: {
  currentTarget: unknown
  pointerId: number
  clientX: number
}) {
  return args as PointerEvent
}

function createHarness(options?: {
  columns?: RegisterColumn[]
}) {
  const columns = ref<RegisterColumn[]>(options?.columns ?? [
    {
      key: 'amount',
      title: 'Amount',
      width: 140,
      minWidth: 100,
    },
  ])
  const localWidths = ref<Record<string, number>>({})

  const resizeState = useRegisterColumnResize({
    columns: computed(() => columns.value),
    localWidths,
    colWidth: (column) => localWidths.value[column.key] ?? column.width ?? 140,
  })

  return {
    columns,
    localWidths,
    resizeState,
  }
}

describe('register column resize', () => {
  let originalHTMLElement: typeof globalThis.HTMLElement | undefined

  beforeEach(() => {
    beforeUnmountCallbacks.length = 0
    originalHTMLElement = globalThis.HTMLElement
    globalThis.HTMLElement = FakeHTMLElement as unknown as typeof HTMLElement
  })

  afterEach(() => {
    globalThis.HTMLElement = originalHTMLElement
  })

  it('tracks pointer movement, clamps widths to min width, and stops on pointerup', () => {
    const handle = new FakeHTMLElement()
    const { localWidths, resizeState } = createHarness()

    resizeState.startResize('amount', pointerEvent({
      currentTarget: handle,
      pointerId: 7,
      clientX: 200,
    }))

    expect(handle.setPointerCapture).toHaveBeenCalledWith(7)

    handle.dispatch('pointermove', pointerEvent({
      currentTarget: handle,
      pointerId: 7,
      clientX: 250,
    }))
    expect(localWidths.value.amount).toBe(190)

    handle.dispatch('pointermove', pointerEvent({
      currentTarget: handle,
      pointerId: 7,
      clientX: 20,
    }))
    expect(localWidths.value.amount).toBe(100)

    handle.dispatch('pointerup', pointerEvent({
      currentTarget: handle,
      pointerId: 7,
      clientX: 20,
    }))
    expect(handle.releasePointerCapture).toHaveBeenCalledWith(7)

    handle.dispatch('pointermove', pointerEvent({
      currentTarget: handle,
      pointerId: 7,
      clientX: 320,
    }))
    expect(localWidths.value.amount).toBe(100)
  })

  it('stops an active session before starting a new one and cleans up on unmount', () => {
    const firstHandle = new FakeHTMLElement()
    const secondHandle = new FakeHTMLElement()
    const { localWidths, resizeState } = createHarness()

    resizeState.startResize('amount', pointerEvent({
      currentTarget: firstHandle,
      pointerId: 1,
      clientX: 100,
    }))

    resizeState.startResize('amount', pointerEvent({
      currentTarget: secondHandle,
      pointerId: 2,
      clientX: 150,
    }))

    expect(firstHandle.releasePointerCapture).toHaveBeenCalledWith(1)
    expect(secondHandle.setPointerCapture).toHaveBeenCalledWith(2)

    firstHandle.dispatch('pointermove', pointerEvent({
      currentTarget: firstHandle,
      pointerId: 1,
      clientX: 260,
    }))
    expect(localWidths.value.amount).toBeUndefined()

    secondHandle.dispatch('pointermove', pointerEvent({
      currentTarget: secondHandle,
      pointerId: 2,
      clientX: 210,
    }))
    expect(localWidths.value.amount).toBe(200)

    runBeforeUnmountHooks()
    expect(secondHandle.releasePointerCapture).toHaveBeenCalledWith(2)
  })
})
