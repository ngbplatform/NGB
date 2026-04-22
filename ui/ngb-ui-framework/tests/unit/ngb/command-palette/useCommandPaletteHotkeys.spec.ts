import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mountedCallbacks = vi.hoisted(() => [] as Array<() => void>)
const beforeUnmountCallbacks = vi.hoisted(() => [] as Array<() => void>)
const hotkeyStore = vi.hoisted(() => ({
  isOpen: false,
  open: vi.fn(),
}))

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

vi.mock('../../../../src/ngb/command-palette/store', () => ({
  useCommandPaletteStore: () => hotkeyStore,
}))

import { useCommandPaletteHotkeys } from '../../../../src/ngb/command-palette/useCommandPaletteHotkeys'

function runMountedHooks() {
  for (const callback of mountedCallbacks.splice(0)) callback()
}

function runBeforeUnmountHooks() {
  for (const callback of beforeUnmountCallbacks.splice(0)) callback()
}

class FakeElement {
  isContentEditable = false
  private readonly selectors = new Set<string>()

  constructor(selectors: string[] = []) {
    selectors.forEach((selector) => this.selectors.add(selector))
  }

  closest(selector: string): FakeElement | null {
    return this.selectors.has(selector) ? this : null
  }
}

describe('useCommandPaletteHotkeys', () => {
  let originalWindow: typeof globalThis.window | undefined
  let originalHTMLElement: typeof globalThis.HTMLElement | undefined
  let listeners: Record<string, EventListener>
  let addEventListenerMock: ReturnType<typeof vi.fn>
  let removeEventListenerMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    mountedCallbacks.length = 0
    beforeUnmountCallbacks.length = 0
    hotkeyStore.isOpen = false
    hotkeyStore.open.mockClear()
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
    } as typeof window
    globalThis.HTMLElement = FakeElement as unknown as typeof HTMLElement
  })

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
    globalThis.HTMLElement = originalHTMLElement as typeof HTMLElement
  })

  it('registers and unregisters the global keydown listener', () => {
    useCommandPaletteHotkeys()
    runMountedHooks()

    expect(addEventListenerMock).toHaveBeenCalledWith('keydown', expect.any(Function))

    runBeforeUnmountHooks()
    expect(removeEventListenerMock).toHaveBeenCalledWith('keydown', expect.any(Function))
  })

  it('opens the palette on ctrl/cmd+k for non-editable targets', () => {
    useCommandPaletteHotkeys()
    runMountedHooks()

    const preventDefault = vi.fn()
    listeners.keydown?.({
      ctrlKey: true,
      metaKey: false,
      altKey: false,
      shiftKey: false,
      key: 'k',
      target: new FakeElement(),
      preventDefault,
    } as unknown as KeyboardEvent)

    expect(preventDefault).toHaveBeenCalledTimes(1)
    expect(hotkeyStore.open).toHaveBeenCalledTimes(1)
  })

  it('ignores the shortcut in editable targets when the palette is closed', () => {
    useCommandPaletteHotkeys()
    runMountedHooks()

    const preventDefault = vi.fn()
    const input = new FakeElement(['input, textarea, select, [contenteditable="true"], [role="textbox"]'])

    listeners.keydown?.({
      ctrlKey: true,
      metaKey: false,
      altKey: false,
      shiftKey: false,
      key: 'k',
      target: input,
      preventDefault,
    } as unknown as KeyboardEvent)

    expect(preventDefault).not.toHaveBeenCalled()
    expect(hotkeyStore.open).not.toHaveBeenCalled()
  })

  it('still opens from editable targets when the palette is already open', () => {
    hotkeyStore.isOpen = true
    useCommandPaletteHotkeys()
    runMountedHooks()

    const preventDefault = vi.fn()
    const textarea = new FakeElement(['input, textarea, select, [contenteditable="true"], [role="textbox"]'])

    listeners.keydown?.({
      ctrlKey: false,
      metaKey: true,
      altKey: false,
      shiftKey: false,
      key: 'K',
      target: textarea,
      preventDefault,
    } as unknown as KeyboardEvent)

    expect(preventDefault).toHaveBeenCalledTimes(1)
    expect(hotkeyStore.open).toHaveBeenCalledTimes(1)
  })

  it('ignores non-matching shortcuts', () => {
    useCommandPaletteHotkeys()
    runMountedHooks()

    const preventDefault = vi.fn()

    listeners.keydown?.({
      ctrlKey: true,
      metaKey: false,
      altKey: true,
      shiftKey: false,
      key: 'k',
      target: new FakeElement(),
      preventDefault,
    } as unknown as KeyboardEvent)

    listeners.keydown?.({
      ctrlKey: true,
      metaKey: false,
      altKey: false,
      shiftKey: false,
      key: 'p',
      target: new FakeElement(),
      preventDefault,
    } as unknown as KeyboardEvent)

    expect(preventDefault).not.toHaveBeenCalled()
    expect(hotkeyStore.open).not.toHaveBeenCalled()
  })
})
