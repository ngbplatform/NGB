import { ref } from 'vue'
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

import { useFloatingDropdownPosition } from '../../../../../src/ngb/primitives/useFloatingDropdownPosition'

function runMountedHooks() {
  for (const callback of mountedCallbacks.splice(0)) callback()
}

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

describe('useFloatingDropdownPosition', () => {
  let originalWindow: typeof globalThis.window | undefined
  let originalHTMLElement: typeof globalThis.HTMLElement | undefined
  let listeners: Record<string, EventListener>
  let addEventListenerMock: ReturnType<typeof vi.fn>
  let removeEventListenerMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    mountedCallbacks.length = 0
    beforeUnmountCallbacks.length = 0
    listeners = {}
    addEventListenerMock = vi.fn((type: string, listener: EventListener) => {
      listeners[type] = listener
    })
    removeEventListenerMock = vi.fn()
    originalWindow = globalThis.window
    originalHTMLElement = globalThis.HTMLElement
    globalThis.window = {
      addEventListener: addEventListenerMock,
      removeEventListener: removeEventListenerMock,
      requestAnimationFrame: vi.fn((callback: FrameRequestCallback) => {
        callback(0)
        return 1
      }),
      cancelAnimationFrame: vi.fn(),
    } as typeof window
    globalThis.HTMLElement = class FakeHTMLElement {} as typeof HTMLElement
  })

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
    globalThis.HTMLElement = originalHTMLElement as typeof HTMLElement
  })

  it('positions the floating overlay from the anchor rect and updates on window changes only when an overlay is visible', () => {
    let rect = { left: 10, bottom: 30, width: 120 }

    const anchor = new globalThis.HTMLElement()
    ;(anchor as HTMLElement).getBoundingClientRect = () => ({
      left: rect.left,
      bottom: rect.bottom,
      width: rect.width,
    } as DOMRect)

    const anchorRef = ref(anchor as HTMLElement)
    const overlayRef = ref<HTMLElement | null>(null)
    const state = useFloatingDropdownPosition(anchorRef, [overlayRef])

    state.updatePosition()
    expect(state.floatingStyle.value).toEqual({
      left: '10px',
      top: '38px',
      width: '120px',
      maxHeight: '288px',
    })

    runMountedHooks()
    expect(addEventListenerMock).toHaveBeenCalledWith('resize', expect.any(Function))
    expect(addEventListenerMock).toHaveBeenCalledWith('scroll', expect.any(Function), true)

    const resizeHandler = listeners.resize
    expect(resizeHandler).toBeTypeOf('function')

    rect = { left: 22, bottom: 40, width: 180 }
    resizeHandler(new Event('resize'))
    expect(state.floatingStyle.value).toEqual({
      left: '10px',
      top: '38px',
      width: '120px',
      maxHeight: '288px',
    })

    overlayRef.value = new globalThis.HTMLElement() as HTMLElement
    resizeHandler(new Event('resize'))
    expect(state.floatingStyle.value).toEqual({
      left: '22px',
      top: '48px',
      width: '180px',
      maxHeight: '288px',
    })
  })

  it('removes window listeners on unmount', () => {
    const anchor = new globalThis.HTMLElement()
    ;(anchor as HTMLElement).getBoundingClientRect = () => ({
      left: 0,
      bottom: 0,
      width: 0,
    } as DOMRect)

    useFloatingDropdownPosition(ref(anchor as HTMLElement), [ref<HTMLElement | null>(new globalThis.HTMLElement() as HTMLElement)])
    runMountedHooks()
    runBeforeUnmountHooks()

    expect(removeEventListenerMock).toHaveBeenCalledWith('resize', expect.any(Function))
    expect(removeEventListenerMock).toHaveBeenCalledWith('scroll', expect.any(Function), true)
  })
})
