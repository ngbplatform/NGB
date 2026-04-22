import { computed, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mountedCallbacks = vi.hoisted(() => [] as Array<() => void>)
const beforeUnmountCallbacks = vi.hoisted(() => [] as Array<() => void>)

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    onMounted: (callback: () => void) => {
      mountedCallbacks.push(callback)
    },
    onBeforeUnmount: (callback: () => void) => {
      beforeUnmountCallbacks.push(callback)
    },
  }
})

import { useRegisterViewport } from '../../../../../src/ngb/components/register/useRegisterViewport'
import type { DisplayRow } from '../../../../../src/ngb/components/register/registerTypes'

type ResizeObserverState = {
  callback: ResizeObserverCallback | null
  observed: unknown[]
  disconnectCount: number
}

function createResizeObserverMock(state: ResizeObserverState) {
  return class ResizeObserverMock {
    constructor(callback: ResizeObserverCallback) {
      state.callback = callback
    }

    observe(target: unknown) {
      state.observed.push(target)
    }

    disconnect() {
      state.disconnectCount += 1
    }
  }
}

function buildRows(count: number): DisplayRow[] {
  return Array.from({ length: count }, (_, index) => ({
    type: 'row' as const,
    key: `row-${index}`,
    __index: index,
  }))
}

function runMountedHooks() {
  for (const callback of mountedCallbacks.splice(0)) callback()
}

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

function createHarness(options?: {
  viewportHeight?: number
  heightPx?: number
  rowHeight?: number
  rowCount?: number
  overscan?: number
}) {
  const viewportElement = {
    clientHeight: options?.viewportHeight ?? 90,
    scrollTop: 0,
  }
  const viewport = ref(viewportElement as unknown as HTMLElement | null)
  const heightPx = ref(options?.heightPx ?? 120)
  const rowHeight = ref(options?.rowHeight ?? 30)
  const displayRows = ref<DisplayRow[]>(buildRows(options?.rowCount ?? 20))

  const viewportState = useRegisterViewport({
    viewport,
    heightPx: computed(() => heightPx.value),
    rowHeight: computed(() => rowHeight.value),
    displayRows: computed(() => displayRows.value),
    overscan: options?.overscan,
  })

  return {
    viewportElement,
    viewport,
    heightPx,
    rowHeight,
    displayRows,
    viewportState,
  }
}

describe('register viewport', () => {
  let originalResizeObserver: typeof globalThis.ResizeObserver | undefined
  let resizeObserverState: ResizeObserverState

  beforeEach(() => {
    mountedCallbacks.length = 0
    beforeUnmountCallbacks.length = 0
    resizeObserverState = {
      callback: null,
      observed: [],
      disconnectCount: 0,
    }
    originalResizeObserver = globalThis.ResizeObserver
    globalThis.ResizeObserver = createResizeObserverMock(resizeObserverState) as typeof ResizeObserver
  })

  afterEach(() => {
    globalThis.ResizeObserver = originalResizeObserver
  })

  it('virtualizes rows using scroll position and reacts to resize observer height changes', () => {
    const { viewportElement, viewportState } = createHarness({
      viewportHeight: 90,
      rowHeight: 30,
      rowCount: 20,
      overscan: 1,
    })

    runMountedHooks()

    expect(resizeObserverState.observed).toEqual([viewportElement])
    expect(viewportState.totalHeight.value).toBe(600)
    expect(viewportState.offsetTop.value).toBe(0)
    expect(viewportState.visibleRows.value.map((row) => row.key)).toEqual([
      'row-0',
      'row-1',
      'row-2',
      'row-3',
      'row-4',
    ])

    viewportElement.scrollTop = 300
    viewportState.onScroll()

    expect(viewportState.offsetTop.value).toBe(270)
    expect(viewportState.visibleRows.value.map((row) => row.key)).toEqual([
      'row-9',
      'row-10',
      'row-11',
      'row-12',
      'row-13',
    ])

    viewportElement.clientHeight = 150
    resizeObserverState.callback?.([] as ResizeObserverEntry[], {} as ResizeObserver)

    expect(viewportState.visibleRows.value.map((row) => row.key)).toEqual([
      'row-9',
      'row-10',
      'row-11',
      'row-12',
      'row-13',
      'row-14',
      'row-15',
    ])
  })

  it('falls back to configured height when no viewport is available and disconnects on unmount', () => {
    const { viewport, viewportState } = createHarness({
      heightPx: 120,
      rowHeight: 30,
      rowCount: 10,
      overscan: 0,
    })

    viewport.value = null
    runMountedHooks()

    expect(resizeObserverState.observed).toEqual([])
    expect(viewportState.visibleRows.value.map((row) => row.key)).toEqual([
      'row-0',
      'row-1',
      'row-2',
      'row-3',
    ])

    runBeforeUnmountHooks()
    expect(resizeObserverState.disconnectCount).toBe(0)
  })
})
