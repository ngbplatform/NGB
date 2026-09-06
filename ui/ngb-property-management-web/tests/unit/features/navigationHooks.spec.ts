import { beforeEach, describe, expect, it, vi } from 'vitest'

const hooks = vi.hoisted(() => ({
  mounted: [] as Array<() => unknown>,
  unmounted: [] as Array<() => unknown>,
  watches: [] as Array<{ source: () => readonly unknown[]; callback: (current: readonly unknown[], previous?: readonly unknown[], onCleanup?: (callback: () => void) => void) => Promise<void>; options: unknown }>,
  migration: null as null | { sources: () => readonly unknown[]; migrate: (values: readonly unknown[]) => unknown },
  read: vi.fn(),
  remove: vi.fn(),
  write: vi.fn(),
}))

vi.mock('vue', () => ({
  onMounted: (callback: () => unknown) => hooks.mounted.push(callback),
  onBeforeUnmount: (callback: () => unknown) => hooks.unmounted.push(callback),
  watch: (source: () => readonly unknown[], callback: never, options: unknown) => hooks.watches.push({ source, callback, options }),
}))

vi.mock('@ngbplatform/ui', () => ({
  normalizeTrashMode: (value: unknown) => `normalized:${String(value)}`,
  readStorageString: hooks.read,
  removeStorageItem: hooks.remove,
  useRouteQueryMigration: (options: never) => { hooks.migration = options },
  writeStorageString: hooks.write,
}))

import { useOpenItemsNavigationRefresh } from '../../../src/features/open-items/useOpenItemsNavigationRefresh'
import { useOpenItemsRouteContext } from '../../../src/features/open-items/useOpenItemsRouteContext'
import { usePropertiesLegacyQueryCompat } from '../../../src/features/properties/usePropertiesLegacyQueryCompat'

describe('property-management navigation hooks', () => {
  beforeEach(() => {
    hooks.mounted = []
    hooks.unmounted = []
    hooks.watches = []
    hooks.migration = null
    hooks.read.mockReset()
    hooks.remove.mockReset()
    hooks.write.mockReset().mockResolvedValue(undefined)
    vi.unstubAllGlobals()
  })

  it('migrates legacy property trash queries without overwriting split filters', () => {
    const route = { query: { trash: 'all', bTrash: undefined, uTrash: undefined } } as never
    const router = {} as never
    usePropertiesLegacyQueryCompat(route, router)

    expect(hooks.migration!.sources()).toEqual(['all', undefined, undefined])
    const migrate = hooks.migration!.migrate
    expect(migrate([null, null, null])).toBeNull()
    expect(migrate(['all', 'active', null])).toEqual({ trash: undefined })
    expect(migrate(['all', null, 'active'])).toEqual({ trash: undefined })
    expect(migrate(['all', null, null])).toEqual({
      trash: undefined,
      bTrash: 'normalized:all',
      uTrash: 'normalized:all',
      bOffset: 0,
      uOffset: 0,
    })
  })

  it('refreshes on route, storage, focus, and visibility signals and cleans up listeners', async () => {
    const enabled = { value: true }
    const refreshFromRoute = { value: false }
    const load = vi.fn().mockResolvedValue(undefined)
    const clear = vi.fn()
    const listeners = new Map<string, () => void>()
    const windowStub = {
      addEventListener: vi.fn((event: string, callback: () => void) => listeners.set(`window:${event}`, callback)),
      removeEventListener: vi.fn(),
    }
    const documentStub = {
      visibilityState: 'hidden',
      addEventListener: vi.fn((event: string, callback: () => void) => listeners.set(`document:${event}`, callback)),
      removeEventListener: vi.fn(),
    }
    vi.stubGlobal('window', windowStub)
    vi.stubGlobal('document', documentStub)

    hooks.read.mockReturnValue('0')
    const state = useOpenItemsNavigationRefresh({ enabled, load, refreshFromRoute, clearRefreshFlagInRoute: clear, sessionStorageKey: 'refresh-key' } as never)
    state.markNeedsRefresh()
    expect(hooks.write).toHaveBeenCalledWith('session', 'refresh-key', '1')
    await state.refreshIfNeededFromNavigation()
    expect(load).not.toHaveBeenCalled()

    hooks.read.mockReturnValue('1')
    await state.refreshIfNeededFromNavigation()
    expect(hooks.remove).toHaveBeenCalledWith('session', 'refresh-key')
    expect(load).toHaveBeenCalledOnce()
    expect(clear).not.toHaveBeenCalled()

    refreshFromRoute.value = true
    await state.refreshIfNeededFromNavigation()
    expect(load).toHaveBeenCalledTimes(2)
    expect(clear).toHaveBeenCalledOnce()
    enabled.value = false
    await state.refreshIfNeededFromNavigation()
    expect(load).toHaveBeenCalledTimes(2)
    enabled.value = true

    await hooks.mounted[0]!()
    expect(windowStub.addEventListener).toHaveBeenCalledWith('focus', expect.any(Function))
    expect(documentStub.addEventListener).toHaveBeenCalledWith('visibilitychange', expect.any(Function))
    listeners.get('window:focus')!()
    listeners.get('document:visibilitychange')!()
    documentStub.visibilityState = 'visible'
    listeners.get('document:visibilitychange')!()
    await Promise.resolve()
    await Promise.resolve()
    expect(load).toHaveBeenCalledTimes(4)

    hooks.unmounted[0]!()
    expect(windowStub.removeEventListener).toHaveBeenCalledWith('focus', expect.any(Function))
    expect(documentStub.removeEventListener).toHaveBeenCalledWith('visibilitychange', expect.any(Function))
  })

  it('handles server rendering, missing storage keys, and initial browser refresh', async () => {
    const load = vi.fn().mockResolvedValue(undefined)
    const first = useOpenItemsNavigationRefresh({
      enabled: { value: true }, load, refreshFromRoute: { value: false }, clearRefreshFlagInRoute: vi.fn(),
    } as never)
    first.markNeedsRefresh()
    await first.refreshIfNeededFromNavigation()
    await hooks.mounted[0]!()
    hooks.unmounted[0]!()
    expect(hooks.write).not.toHaveBeenCalled()
    expect(load).not.toHaveBeenCalled()

    const windowStub = { addEventListener: vi.fn(), removeEventListener: vi.fn() }
    const documentStub = { visibilityState: 'visible', addEventListener: vi.fn(), removeEventListener: vi.fn() }
    vi.stubGlobal('window', windowStub)
    vi.stubGlobal('document', documentStub)
    hooks.read.mockReturnValue('1')
    const second = useOpenItemsNavigationRefresh({
      enabled: { value: true }, load, refreshFromRoute: { value: false }, clearRefreshFlagInRoute: vi.fn(), sessionStorageKey: 'key',
    } as never)
    await hooks.mounted[1]!()
    expect(load).toHaveBeenCalledOnce()
    hooks.unmounted[1]!()
    expect(second.markNeedsRefresh).toEqual(expect.any(Function))
  })

  it('synchronizes route contexts across skip, initial, unchanged, and changed transitions', async () => {
    const hydrate = vi.fn().mockResolvedValue(undefined)
    const load = vi.fn().mockResolvedValue(undefined)
    const sync = vi.fn().mockResolvedValue(undefined)
    const afterSync = vi.fn().mockResolvedValue(undefined)
    const autoOpen = vi.fn(() => true)
    const clear = vi.fn()
    let shouldSkip = true
    useOpenItemsRouteContext({
      source: () => ['lease-1', 'party-1'] as const,
      contextKeyCount: 2,
      hydrateContext: hydrate,
      load,
      preferredTab: { value: 'credits' },
      currentError: { value: 'failure' },
      syncAfterContextLoad: sync,
      autoOpenApply: autoOpen,
      clearAutoOpenApplyInRoute: clear,
      shouldSkip: () => shouldSkip,
      afterSync,
    } as never)
    const watcher = hooks.watches[0]!
    expect(watcher.options).toEqual({ immediate: true })
    expect(watcher.source()).toEqual(['lease-1', 'party-1'])
    await watcher.callback(['lease-1', 'party-1'])
    expect(hydrate).not.toHaveBeenCalled()

    shouldSkip = false
    await watcher.callback(['lease-1', 'party-1'])
    expect(sync).toHaveBeenLastCalledWith(expect.objectContaining({
      contextChanged: true, preferredTab: 'credits', autoOpenApply: true, currentError: 'failure',
    }))
    const firstSyncArgs = sync.mock.calls.at(-1)![0]
    firstSyncArgs.clearAutoOpenApplyInRoute()
    expect(clear).toHaveBeenCalledWith(['lease-1', 'party-1'], undefined)
    expect(afterSync).toHaveBeenCalledOnce()

    await watcher.callback(['lease-1', 'party-1'], ['lease-1', 'party-1'])
    expect(sync).toHaveBeenLastCalledWith(expect.objectContaining({ contextChanged: false }))
    await watcher.callback(['lease-1', 'party-2'], ['lease-1', 'party-1'])
    expect(sync).toHaveBeenLastCalledWith(expect.objectContaining({ contextChanged: true }))

    useOpenItemsRouteContext({
      source: () => ['same'] as const,
      contextKeyCount: 1,
      hydrateContext: hydrate,
      load,
      preferredTab: { value: null },
      currentError: { value: null },
      syncAfterContextLoad: sync,
      autoOpenApply: () => false,
      clearAutoOpenApplyInRoute: vi.fn(),
    } as never)
    await hooks.watches[1]!.callback(['same'], ['same'])
    expect(sync).toHaveBeenLastCalledWith(expect.objectContaining({ contextChanged: false, autoOpenApply: false }))
  })

  it('cancels route synchronization after each asynchronous boundary', async () => {
    let releaseHydrate!: () => void
    let releaseLoad!: () => void
    let releaseSync!: () => void
    const hydrate = vi.fn(() => new Promise<void>((resolve) => { releaseHydrate = resolve }))
    const load = vi.fn(() => new Promise<void>((resolve) => { releaseLoad = resolve }))
    const sync = vi.fn(() => new Promise<void>((resolve) => { releaseSync = resolve }))
    const afterSync = vi.fn()

    useOpenItemsRouteContext({
      source: () => ['lease'] as const,
      contextKeyCount: 1,
      hydrateContext: hydrate,
      load,
      preferredTab: { value: null },
      currentError: { value: null },
      syncAfterContextLoad: sync,
      autoOpenApply: () => false,
      clearAutoOpenApplyInRoute: vi.fn(),
      afterSync,
    } as never)
    const watcher = hooks.watches[0]!

    let cleanup!: () => void
    const first = watcher.callback(['lease'], undefined, (callback) => { cleanup = callback })
    cleanup()
    releaseHydrate()
    await first
    expect(load).not.toHaveBeenCalled()

    const second = watcher.callback(['lease'], undefined, (callback) => { cleanup = callback })
    releaseHydrate()
    await Promise.resolve()
    cleanup()
    releaseLoad()
    await second
    expect(sync).not.toHaveBeenCalled()

    const third = watcher.callback(['lease'], undefined, (callback) => { cleanup = callback })
    releaseHydrate()
    await Promise.resolve()
    releaseLoad()
    await Promise.resolve()
    cleanup()
    releaseSync()
    await third
    expect(afterSync).not.toHaveBeenCalled()
  })
})
