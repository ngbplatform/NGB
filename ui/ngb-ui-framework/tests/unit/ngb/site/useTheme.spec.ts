import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mountedCallbacks = vi.hoisted(() => [] as Array<() => void>)
const storageMocks = vi.hoisted(() => ({
  readCookie: vi.fn(),
  readStorageString: vi.fn(),
  writeCookie: vi.fn(),
  writeStorageString: vi.fn(() => true),
}))

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    onMounted: (callback: () => void) => {
      mountedCallbacks.push(callback)
    },
  }
})

vi.mock('../../../../src/ngb/utils/storage', () => storageMocks)

import { useTheme, type ThemeMode } from '../../../../src/ngb/site/useTheme'

function runMountedHook() {
  const callback = mountedCallbacks.shift()
  expect(callback).toBeTypeOf('function')
  callback?.()
}

function installBrowserGlobals(options: {
  hostname?: string
  protocol?: string
  prefersDark?: boolean
  withMatchMedia?: boolean
} = {}) {
  const classes = new Set<string>()
  globalThis.document = {
    documentElement: {
      classList: {
        add: (value: string) => classes.add(value),
        remove: (value: string) => classes.delete(value),
        contains: (value: string) => classes.has(value),
      },
    },
  } as unknown as Document
  globalThis.window = {
    location: {
      hostname: options.hostname ?? 'localhost',
      protocol: options.protocol ?? 'http:',
    },
    matchMedia: options.withMatchMedia === false
      ? undefined
      : vi.fn(() => ({ matches: options.prefersDark ?? false })),
  } as unknown as Window & typeof globalThis

  return classes
}

describe('useTheme boundaries', () => {
  const originalDocument = globalThis.document
  const originalWindow = globalThis.window

  beforeEach(() => {
    mountedCallbacks.length = 0
    vi.clearAllMocks()
    storageMocks.readCookie.mockReturnValue(null)
    storageMocks.readStorageString.mockReturnValue(null)
    installBrowserGlobals()
  })

  afterEach(() => {
    globalThis.document = originalDocument
    globalThis.window = originalWindow
  })

  it.each<ThemeMode>(['light', 'dark', 'system'])('restores valid %s mode from local storage', (saved) => {
    storageMocks.readStorageString.mockReturnValue(saved)
    storageMocks.readCookie.mockReturnValue('dark')
    const theme = useTheme()

    runMountedHook()

    expect(theme.mode.value).toBe(saved)
    expect(storageMocks.readCookie).not.toHaveBeenCalled()
  })

  it.each<ThemeMode>(['light', 'dark', 'system'])('restores valid %s mode from the cookie', (saved) => {
    storageMocks.readStorageString.mockReturnValue('invalid')
    storageMocks.readCookie.mockReturnValue(saved)
    const theme = useTheme()

    runMountedHook()

    expect(theme.mode.value).toBe(saved)
  })

  it('keeps system mode for invalid persisted values and toggles in both directions', () => {
    installBrowserGlobals({ prefersDark: false })
    storageMocks.readStorageString.mockReturnValue('invalid')
    storageMocks.readCookie.mockReturnValue('sepia')
    const theme = useTheme()

    runMountedHook()
    expect(theme.mode.value).toBe('system')
    expect(theme.resolved.value).toBe('light')

    theme.toggle()
    expect(theme.mode.value).toBe('dark')
    theme.toggle()
    expect(theme.mode.value).toBe('light')
  })

  it('uses a secure shared cookie for a normal HTTPS hostname', () => {
    installBrowserGlobals({
      hostname: ' App.Example.COM ',
      protocol: 'https:',
      prefersDark: true,
    })

    const theme = useTheme()

    expect(theme.resolved.value).toBe('dark')
    expect(storageMocks.writeCookie).toHaveBeenCalledWith('ngb.theme', 'system', expect.objectContaining({
      secure: true,
      domain: 'app.example.com',
    }))
  })

  it.each([
    ['localhost', 'http:'],
    ['127.0.0.1', 'http:'],
    ['::1', 'http:'],
    ['devbox', 'http:'],
    ['', 'http:'],
  ])('does not write a shared cookie for hostname %j', (hostname, protocol) => {
    installBrowserGlobals({ hostname, protocol })

    useTheme()

    expect(storageMocks.writeCookie).toHaveBeenCalledTimes(1)
    expect(storageMocks.writeCookie).toHaveBeenCalledWith('ngb.theme', 'system', expect.objectContaining({
      secure: false,
    }))
    expect(storageMocks.writeCookie.mock.calls[0]?.[2]).not.toHaveProperty('domain')
  })

  it('falls back to light when matchMedia is unavailable', () => {
    installBrowserGlobals({ withMatchMedia: false })

    const theme = useTheme()

    expect(theme.resolved.value).toBe('light')
  })

  it('does not touch cookies or the DOM without browser globals', () => {
    globalThis.document = undefined as never
    globalThis.window = undefined as never

    const theme = useTheme()

    expect(theme.resolved.value).toBe('light')
    expect(storageMocks.writeCookie).not.toHaveBeenCalled()
    runMountedHook()
  })

  it('does not persist a cookie when document exists but window is unavailable', () => {
    globalThis.window = undefined as never

    useTheme()

    expect(storageMocks.writeCookie).not.toHaveBeenCalled()
  })
})
