import { nextTick, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mountedCallbacks = vi.hoisted(() => [] as Array<() => void>)
const beforeUnmountCallbacks = vi.hoisted(() => [] as Array<() => void>)

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    onMounted: (callback: () => void) => mountedCallbacks.push(callback),
    onBeforeUnmount: (callback: () => void) => beforeUnmountCallbacks.push(callback),
  }
})

import { useVirtualWorkCenterFeed } from '../../../../src/ngb/work-center/useVirtualWorkCenterFeed'

type Rect = Pick<DOMRect, 'top' | 'bottom'>

class FakeElement {
  dataset: Record<string, string> = {}
  clientHeight = 0
  rect: Rect = { top: 0, bottom: 0 }
  addEventListener = vi.fn()
  removeEventListener = vi.fn()

  getBoundingClientRect(): DOMRect {
    return this.rect as DOMRect
  }
}

type ObserverHarness = {
  callbacks: ResizeObserverCallback[]
  observed: unknown[]
  unobserved: unknown[]
  disconnects: number
}

function createResizeObserverMock(state: ObserverHarness) {
  return class ResizeObserverMock {
    constructor(callback: ResizeObserverCallback) {
      state.callbacks.push(callback)
    }

    observe(target: unknown) {
      state.observed.push(target)
    }

    unobserve(target: unknown) {
      state.unobserved.push(target)
    }

    disconnect() {
      state.disconnects += 1
    }
  }
}

function runMountedHooks() {
  for (const callback of mountedCallbacks.splice(0)) callback()
}

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

describe('virtual work-center feed', () => {
  const originalElement = globalThis.HTMLElement
  const originalResizeObserver = globalThis.ResizeObserver
  const originalRequestAnimationFrame = globalThis.requestAnimationFrame
  const originalCancelAnimationFrame = globalThis.cancelAnimationFrame
  const originalWindow = (globalThis as { window?: unknown }).window
  let observerState: ObserverHarness
  let frames: Array<FrameRequestCallback | undefined>

  beforeEach(() => {
    mountedCallbacks.length = 0
    beforeUnmountCallbacks.length = 0
    observerState = { callbacks: [], observed: [], unobserved: [], disconnects: 0 }
    frames = []
    globalThis.HTMLElement = FakeElement as unknown as typeof HTMLElement
    globalThis.ResizeObserver = createResizeObserverMock(observerState) as typeof ResizeObserver
    globalThis.requestAnimationFrame = vi.fn((callback: FrameRequestCallback) => {
      frames.push(callback)
      return frames.length
    })
    globalThis.cancelAnimationFrame = vi.fn((handle: number) => {
      frames[handle - 1] = undefined
    })
    Reflect.deleteProperty(globalThis, 'window')
  })

  afterEach(() => {
    globalThis.HTMLElement = originalElement
    globalThis.ResizeObserver = originalResizeObserver
    globalThis.requestAnimationFrame = originalRequestAnimationFrame
    globalThis.cancelAnimationFrame = originalCancelAnimationFrame
    if (originalWindow === undefined) Reflect.deleteProperty(globalThis, 'window')
    else Object.assign(globalThis, { window: originalWindow })
  })

  it('virtualizes measured rows, coalesces scrolling, and removes stale elements', async () => {
    const items = ref(Array.from({ length: 100 }, (_, index) => ({ id: `item-${index}` })))
    const host = new FakeElement()
    host.clientHeight = 120
    host.rect = { top: 100, bottom: 240 }
    const list = new FakeElement()
    list.rect = { top: -900, bottom: 1_100 }
    const scrollHost = ref(host as unknown as HTMLElement | null)
    const listElement = ref(list as unknown as HTMLElement | null)
    const feed = useVirtualWorkCenterFeed({
      items,
      scrollHost,
      listElement,
      getKey: (item) => item.id,
      estimatedItemHeight: 20,
    })

    runMountedHooks()
    await nextTick()

    expect(observerState.callbacks).toHaveLength(2)
    expect(observerState.observed).toHaveLength(1)
    expect(host.addEventListener).toHaveBeenCalledWith('scroll', expect.any(Function), { passive: true })
    expect(feed.virtualEntries.value[0]?.key).toBe('item-25')
    expect(feed.topSpacerHeight.value).toBe(500)
    expect(feed.bottomSpacerHeight.value).toBeGreaterThan(0)

    const measured = new FakeElement()
    const replacement = new FakeElement()
    const retained = new FakeElement()
    feed.setItemElement('item-25', measured)
    feed.setItemElement('item-25', replacement)
    feed.setItemElement('item-26', retained)
    feed.setItemElement('not-an-element', null)
    expect(measured.dataset.workCenterVirtualKey).toBe('item-25')
    expect(observerState.unobserved.some((target) => target === measured)).toBe(true)

    const noKey = new FakeElement()
    const zeroHeight = new FakeElement()
    zeroHeight.dataset.workCenterVirtualKey = 'zero'
    observerState.callbacks[0]!([
      { target: noKey, contentRect: { height: 10 } },
      { target: zeroHeight, contentRect: { height: 0 } },
      { target: replacement, borderBoxSize: [{ blockSize: 40 }] },
      { target: retained, contentRect: { height: 30 } },
    ] as unknown as ResizeObserverEntry[], {} as ResizeObserver)
    observerState.callbacks[0]!([
      { target: replacement, contentRect: { height: 40 } },
    ] as unknown as ResizeObserverEntry[], {} as ResizeObserver)
    expect(feed.topSpacerHeight.value).toBe(500)

    const onScroll = host.addEventListener.mock.calls[0]![1] as () => void
    onScroll()
    onScroll()
    expect(globalThis.requestAnimationFrame).toHaveBeenCalledOnce()
    frames[0]!((performance.now()))
    expect(globalThis.requestAnimationFrame).toHaveBeenCalledOnce()

    items.value = items.value.filter((item) => item.id !== 'item-25')
    await nextTick()
    await nextTick()
    expect(observerState.unobserved.some((target) => target === replacement)).toBe(true)

    onScroll()
    runBeforeUnmountHooks()
    expect(globalThis.cancelAnimationFrame).toHaveBeenCalledWith(2)
    expect(host.removeEventListener).toHaveBeenCalledWith('scroll', onScroll)
    expect(observerState.disconnects).toBe(2)
  })

  it('returns every item below the threshold and tolerates missing browser primitives and elements', async () => {
    globalThis.ResizeObserver = undefined as unknown as typeof ResizeObserver
    const items = ref([{ id: 'one' }, { id: 'two' }])
    const scrollHost = ref<HTMLElement | null>(null)
    const listElement = ref<HTMLElement | null>(null)
    const feed = useVirtualWorkCenterFeed({
      items,
      scrollHost,
      listElement,
      getKey: (item) => item.id,
      estimatedItemHeight: 25,
    })

    runMountedHooks()
    await nextTick()
    expect(feed.virtualEntries.value.map((entry) => entry.key)).toEqual(['one', 'two'])
    expect(feed.topSpacerHeight.value).toBe(0)
    expect(feed.bottomSpacerHeight.value).toBe(0)

    items.value = [{ id: 'two' }]
    await nextTick()
    await nextTick()
    runBeforeUnmountHooks()
  })

  it('clips the viewport to the visible browser area', async () => {
    Object.assign(globalThis, { window: { innerHeight: 150 } })
    const items = ref(Array.from({ length: 100 }, (_, index) => ({ id: String(index) })))
    const host = new FakeElement()
    host.clientHeight = 200
    host.rect = { top: -50, bottom: 250 }
    const list = new FakeElement()
    list.rect = { top: -150, bottom: 1_850 }
    useVirtualWorkCenterFeed({
      items,
      scrollHost: ref(host as unknown as HTMLElement),
      listElement: ref(list as unknown as HTMLElement),
      getKey: (item) => item.id,
      estimatedItemHeight: 20,
    })

    runMountedHooks()
    await nextTick()
    observerState.callbacks[1]!([] as ResizeObserverEntry[], {} as ResizeObserver)
  })
})
