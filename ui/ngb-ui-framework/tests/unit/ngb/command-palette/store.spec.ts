import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  config: {
    router: {
      currentRoute: {
        value: {
          fullPath: '/home',
        },
      },
      push: vi.fn(),
      resolve: vi.fn((route: string) => ({ href: `https://ngb.test${route}` })),
    },
    recentStorageKey: 'ngb:test:command-palette',
    searchRemote: undefined as undefined | ((request: unknown, signal?: AbortSignal) => Promise<unknown>),
    loadReportItems: vi.fn(),
    buildHeuristicCurrentActions: vi.fn(),
    getMenuGroups: () => mocks.menuStore.groups,
    favoriteItems: [] as unknown[],
    createItems: [] as unknown[],
    specialPageItems: [] as unknown[],
  },
  menuStore: {
    groups: [] as unknown[],
  },
  loadRecent: vi.fn(),
  saveRecent: vi.fn(),
}))

vi.mock('../../../../src/ngb/command-palette/config', () => ({
  getConfiguredNgbCommandPalette: () => mocks.config,
}))

vi.mock('../../../../src/ngb/command-palette/storage', () => ({
  loadCommandPaletteRecent: mocks.loadRecent,
  saveCommandPaletteRecent: mocks.saveRecent,
}))

import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'
import type { CommandPaletteItem } from '../../../../src/ngb/command-palette/types'

async function flushMicrotasks() {
  await Promise.resolve()
  await Promise.resolve()
}

function makeItem(overrides: Partial<CommandPaletteItem> = {}): CommandPaletteItem {
  return {
    key: 'command:test',
    group: 'actions',
    kind: 'command',
    scope: 'commands',
    title: 'Test command',
    subtitle: 'Test subtitle',
    icon: 'search',
    badge: 'Test',
    hint: null,
    route: null,
    commandCode: 'test',
    status: 'Ready',
    openInNewTabSupported: false,
    keywords: ['test'],
    defaultRank: 500,
    score: 0,
    source: 'local',
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

describe('command palette store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useRealTimers()
    setActivePinia(createPinia())

    mocks.config.router.currentRoute.value.fullPath = '/home'
    mocks.config.getMenuGroups = () => mocks.menuStore.groups
    mocks.config.searchRemote = undefined
    mocks.config.loadReportItems = vi.fn().mockResolvedValue([
      {
        key: 'report:occupancy',
        group: 'reports',
        kind: 'report',
        scope: 'reports',
        title: 'Occupancy Summary',
        subtitle: 'Portfolio occupancy',
        icon: 'bar-chart',
        badge: 'Report',
        hint: null,
        route: '/reports/occupancy',
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: ['occupancy'],
        defaultRank: 700,
      },
    ])
    mocks.config.buildHeuristicCurrentActions = vi.fn().mockReturnValue([
      {
        key: 'heuristic:refresh',
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Refresh current page',
        subtitle: 'Reload this page',
        icon: 'refresh',
        badge: 'Refresh',
        hint: null,
        route: null,
        commandCode: 'refresh',
        status: null,
        openInNewTabSupported: false,
        keywords: ['refresh'],
        defaultRank: 970,
      },
    ])
    mocks.config.favoriteItems = [
      {
        key: 'favorite:settings',
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Open settings',
        subtitle: 'Favorite command',
        icon: 'settings',
        badge: 'Favorite',
        hint: null,
        route: null,
        commandCode: 'settings',
        status: null,
        openInNewTabSupported: false,
        keywords: ['settings'],
        defaultRank: 820,
      },
    ]
    mocks.config.createItems = [
      {
        key: 'create:invoice',
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Create invoice',
        subtitle: 'Create a new invoice',
        icon: 'plus',
        badge: 'Create',
        hint: null,
        route: null,
        commandCode: 'create-invoice',
        status: null,
        openInNewTabSupported: false,
        keywords: ['create', 'invoice'],
        defaultRank: 780,
      },
    ]
    mocks.config.specialPageItems = [
      {
        key: 'page:special-settings',
        group: 'go-to',
        kind: 'page',
        scope: 'pages',
        title: 'Settings',
        subtitle: 'Admin',
        icon: 'settings',
        badge: 'Page',
        hint: null,
        route: '/settings',
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: ['settings'],
        defaultRank: 560,
      },
    ]

    mocks.menuStore.groups = [
      {
        label: 'Home',
        ordinal: 0,
        icon: 'home',
        items: [
          { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
        ],
      },
      {
        label: 'Payables',
        ordinal: 10,
        icon: 'wallet',
        items: [
          { kind: 'page', code: 'payables-open-items', label: 'Payables', route: '/payables/open-items', icon: 'wallet', ordinal: 0 },
        ],
      },
    ]

    mocks.loadRecent.mockReturnValue([
      {
        key: 'page:home',
        kind: 'page',
        scope: 'pages',
        title: 'Home',
        subtitle: 'Recent page',
        icon: 'home',
        badge: 'Recent',
        route: '/home',
        status: null,
        openInNewTabSupported: true,
        timestamp: '2026-04-08T12:00:00.000Z',
      },
    ])
  })

  it('hydrates local groups from explicit context, favorites, menu pages, reports, and recents', async () => {
    const store = useCommandPaletteStore()

    store.setExplicitContext('spec', {
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: 'doc-1',
      title: 'Invoice INV-001',
      actions: [
        {
          key: 'current:approve',
          group: 'actions',
          kind: 'command',
          scope: 'commands',
          title: 'Approve invoice',
          subtitle: 'Approve this draft',
          icon: 'check',
          badge: 'Approve',
          hint: null,
          route: null,
          commandCode: 'approve',
          status: null,
          openInNewTabSupported: false,
          keywords: ['approve'],
          defaultRank: 990,
          isCurrentContext: true,
        },
      ],
    })

    store.open()
    await flushMicrotasks()

    expect(store.isOpen).toBe(true)
    expect(store.focusRequestKey).toBe(1)
    expect(mocks.loadRecent).toHaveBeenCalledWith('ngb:test:command-palette')
    expect(mocks.config.loadReportItems).toHaveBeenCalledTimes(1)
    expect(mocks.config.buildHeuristicCurrentActions).toHaveBeenCalledWith('/home')

    expect(store.groups.map((group) => group.code)).toEqual(['actions', 'go-to', 'reports', 'recent'])
    expect(store.flatItems.some((item) => item.title === 'Approve invoice')).toBe(true)
    expect(store.flatItems.some((item) => item.title === 'Refresh current page')).toBe(true)
    expect(store.flatItems.some((item) => item.title === 'Create invoice')).toBe(true)
    expect(store.flatItems.some((item) => item.title === 'Payables')).toBe(true)
    expect(store.flatItems.some((item) => item.title === 'Occupancy Summary')).toBe(true)
    expect(store.flatItems.some((item) => item.title === 'Home')).toBe(true)
  })

  it('filters local groups by scoped queries and keeps active index bounded', async () => {
    const store = useCommandPaletteStore()

    store.open()
    await flushMicrotasks()

    store.setQuery('/payables')

    expect(store.cleanQuery).toBe('payables')
    expect(store.activeScope).toBe('pages')
    expect(store.groups.map((group) => group.code)).toEqual(['go-to'])
    expect(store.flatItems.some((item) => item.scope === 'commands')).toBe(false)

    store.setActiveIndex(99)
    expect(store.activeIndex).toBe(store.flatItems.length - 1)
    store.moveActive(-999)
    expect(store.activeIndex).toBe(0)
  })

  it('executes route items, closes the dialog, and records recent entries', async () => {
    const store = useCommandPaletteStore()

    store.open()
    await flushMicrotasks()
    store.setQuery('payables')

    const payablesItem = store.flatItems.find((item) => item.title === 'Payables')
    expect(payablesItem).toBeTruthy()

    await store.executeItem(payablesItem!)

    expect(mocks.config.router.push).toHaveBeenCalledWith('/payables/open-items')
    expect(store.isOpen).toBe(false)
    expect(store.query).toBe('')
    expect(mocks.saveRecent).toHaveBeenCalledWith(
      'ngb:test:command-palette',
      expect.arrayContaining([
        expect.objectContaining({
          key: payablesItem!.key,
          title: 'Payables',
          route: '/payables/open-items',
        }),
      ]),
    )
  })

  it('does not re-add denied special pages, favorites, or recent routes', async () => {
    mocks.config.favoriteItems = [
      {
        key: 'favorite:period-closing',
        group: 'actions',
        kind: 'page',
        scope: 'pages',
        title: 'Period Close',
        subtitle: 'Setup & Controls',
        icon: 'calendar-check',
        badge: 'Favorite',
        hint: null,
        route: '/admin/accounting/period-closing',
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: ['period close'],
        defaultRank: 820,
      },
    ]
    mocks.config.createItems = []
    mocks.config.loadReportItems = vi.fn().mockResolvedValue([])
    mocks.config.buildHeuristicCurrentActions = vi.fn().mockReturnValue([])
    mocks.config.specialPageItems = [
      {
        key: 'page:posting-log',
        group: 'go-to',
        kind: 'page',
        scope: 'pages',
        title: 'Posting Log',
        subtitle: 'Setup & Controls',
        icon: 'history',
        badge: 'Page',
        hint: null,
        route: '/reports/accounting.posting_log',
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: ['posting log'],
        defaultRank: 560,
      },
      {
        key: 'page:period-closing',
        group: 'go-to',
        kind: 'page',
        scope: 'pages',
        title: 'Period Close',
        subtitle: 'Setup & Controls',
        icon: 'calendar-check',
        badge: 'Page',
        hint: null,
        route: '/admin/accounting/period-closing',
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: ['period close'],
        defaultRank: 559,
      },
    ]
    mocks.menuStore.groups = [
      {
        label: 'Setup & Controls',
        ordinal: 10,
        icon: 'settings',
        items: [
          {
            kind: 'admin',
            code: 'accounting.posting_log',
            label: 'Posting Log',
            route: '/admin/accounting/posting-log',
            icon: 'history',
            ordinal: 10,
          },
        ],
      },
    ]
    mocks.loadRecent.mockReturnValue([
      {
        key: 'page:period-closing',
        kind: 'page',
        scope: 'pages',
        title: 'Period Close',
        subtitle: 'Recent page',
        icon: 'calendar-check',
        badge: 'Recent',
        route: '/admin/accounting/period-closing',
        status: null,
        openInNewTabSupported: true,
        timestamp: '2026-06-19T12:00:00.000Z',
      },
    ])

    const store = useCommandPaletteStore()

    store.open()
    await flushMicrotasks()

    const titles = store.flatItems.map((item) => item.title)
    expect(titles.filter((title) => title === 'Posting Log')).toHaveLength(1)
    expect(titles).not.toContain('Period Close')
  })

  it('opens route items in a new tab when requested and skips in-app navigation', async () => {
    const previousWindow = globalThis.window
    const open = vi.fn()
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: { open },
    })

    try {
      const store = useCommandPaletteStore()

      store.open()
      await flushMicrotasks()
      store.setQuery('payables')

      const payablesItem = store.flatItems.find((item) => item.title === 'Payables')
      expect(payablesItem).toBeTruthy()

      await store.executeItem(payablesItem!, 'new-tab')

      expect(open).toHaveBeenCalledWith('https://ngb.test/payables/open-items', '_blank', 'noopener,noreferrer')
      expect(mocks.config.router.push).not.toHaveBeenCalled()
      expect(store.isOpen).toBe(false)
      expect(mocks.saveRecent).toHaveBeenCalledWith(
        'ngb:test:command-palette',
        expect.arrayContaining([
          expect.objectContaining({
            key: payablesItem!.key,
            route: '/payables/open-items',
            openInNewTabSupported: true,
          }),
        ]),
      )
    } finally {
      if (previousWindow === undefined) {
        Reflect.deleteProperty(globalThis, 'window')
      } else {
        Object.defineProperty(globalThis, 'window', {
          configurable: true,
          value: previousWindow,
        })
      }
    }
  })

  it('debounces remote search, passes normalized context, and merges remote groups', async () => {
    vi.useFakeTimers()

    const searchRemote = vi.fn().mockResolvedValue({
      groups: [
        {
          code: 'documents',
          label: 'Documents',
          items: [
            {
              key: 'remote:invoice:1',
              kind: 'document',
              title: 'Invoice INV-001',
              subtitle: 'Remote document',
              icon: 'file',
              badge: 'Document',
              route: '/documents/pm.invoice/doc-1',
              commandCode: null,
              status: null,
              openInNewTabSupported: true,
              score: 0.92,
            },
          ],
        },
        {
          code: 'reports',
          label: 'Reports',
          items: [
            {
              key: 'remote:report:1',
              kind: 'report',
              title: 'Should be filtered',
              openInNewTabSupported: true,
              score: 0.8,
            },
          ],
        },
      ],
    })
    mocks.config.searchRemote = searchRemote

    const store = useCommandPaletteStore()
    store.setExplicitContext('spec', {
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: 'not-a-guid',
      title: 'Invoice INV-001',
      actions: [],
    })
    store.open()
    store.setCurrentRoute('/documents/pm.invoice/doc-1')
    store.setQuery('invoice')
    await flushMicrotasks()

    expect(store.showRemoteLoading).toBe(true)

    await vi.advanceTimersByTimeAsync(160)
    await flushMicrotasks()

    expect(searchRemote).toHaveBeenCalledWith({
      query: 'invoice',
      scope: null,
      limit: 20,
      currentRoute: '/documents/pm.invoice/doc-1',
      context: {
        entityType: 'document',
        documentType: 'pm.invoice',
        catalogType: null,
        entityId: null,
      },
    }, expect.any(AbortSignal))
    expect(store.groups.some((group) => group.code === 'documents')).toBe(true)
    expect(store.flatItems.some((item) => item.key === 'remote:invoice:1')).toBe(true)
    expect(store.flatItems.some((item) => item.key === 'remote:report:1')).toBe(false)
  })

  it('handles empty configuration, hydration idempotency, context ownership, and empty selection movement', async () => {
    mocks.config.router.currentRoute.value.fullPath = ''
    Reflect.set(mocks.config, 'getMenuGroups', undefined)
    Reflect.set(mocks.config, 'loadReportItems', undefined)
    Reflect.set(mocks.config, 'buildHeuristicCurrentActions', undefined)
    Reflect.set(mocks.config, 'favoriteItems', undefined)
    Reflect.set(mocks.config, 'createItems', undefined)
    Reflect.set(mocks.config, 'specialPageItems', undefined)
    mocks.loadRecent.mockReturnValue([])

    const store = useCommandPaletteStore()
    await store.hydrate()
    await store.hydrate()

    store.setCurrentRoute('')
    store.setQuery('retained query')
    store.open()
    await flushMicrotasks()
    expect(store.query).toBe('retained query')

    store.setQuery('')
    store.setExplicitContext('owner', {
      actions: [makeItem({ key: 'context:only', title: 'Context only' })],
    })
    store.clearExplicitContext('different-owner')
    expect(store.flatItems.some((item) => item.key === 'context:only')).toBe(true)

    store.clearExplicitContext('owner')
    await flushMicrotasks()
    expect(store.hasResults).toBe(false)
    store.moveActive(1)
    store.setActiveIndex(4)
    await store.executeActive()
    expect(store.activeIndex).toBe(0)
  })

  it('recovers report loading after a concurrent rejected load and then skips already loaded reports', async () => {
    const reportLoad = deferred<unknown[]>()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    mocks.config.loadReportItems = vi.fn().mockReturnValueOnce(reportLoad.promise).mockResolvedValue([])

    const store = useCommandPaletteStore()
    store.open()
    store.open()
    expect(mocks.config.loadReportItems).toHaveBeenCalledTimes(1)

    reportLoad.reject(new Error('Reports unavailable'))
    await flushMicrotasks()
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ message: 'Reports unavailable' }))

    store.open()
    await flushMicrotasks()
    expect(mocks.config.loadReportItems).toHaveBeenCalledTimes(2)
    store.open()
    expect(mocks.config.loadReportItems).toHaveBeenCalledTimes(2)
  })

  it('executes perform items, handles failures, tracks every recent kind, and supports a server environment', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const perform = vi.fn().mockResolvedValue(undefined)
    const store = useCommandPaletteStore()

    await store.executeItem(makeItem({ key: 'perform:success', perform }))
    expect(perform).toHaveBeenCalledTimes(1)

    await store.executeItem(makeItem({
      key: 'perform:failure',
      perform: vi.fn().mockRejectedValue(new Error('Action failed')),
    }))
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ message: 'Action failed' }))

    await store.executeItem(makeItem({ key: 'untracked:command' }))
    for (const kind of ['page', 'document', 'catalog', 'report', 'recent'] as const) {
      await store.executeItem(makeItem({
        key: `recent:${kind}`,
        kind,
        scope: kind === 'document' ? 'documents' : kind === 'catalog' ? 'catalogs' : kind === 'report' ? 'reports' : 'pages',
        route: `/allowed/${kind}`,
        subtitle: undefined,
        icon: undefined,
        badge: undefined,
        status: undefined,
      }))
    }
    await store.executeItem(makeItem({
      key: 'recent:page',
      kind: 'page',
      scope: 'pages',
      route: '/allowed/page',
    }))

    const previousWindow = globalThis.window
    Reflect.deleteProperty(globalThis, 'window')
    try {
      await store.executeItem(makeItem({
        key: 'server:new-tab',
        kind: 'page',
        scope: 'pages',
        route: '/server-route',
        openInNewTabSupported: true,
      }), 'new-tab')
    } finally {
      if (previousWindow !== undefined) Object.defineProperty(globalThis, 'window', {
        configurable: true,
        value: previousWindow,
      })
    }

    expect(mocks.saveRecent).toHaveBeenCalled()
    expect(mocks.saveRecent.mock.calls.at(-1)?.[1]).toHaveLength(6)
  })

  it('covers remote scopes, group kinds, optional fields, aborts, stale responses, and friendly errors', async () => {
    vi.useFakeTimers()
    const first = deferred<{ groups: unknown[] }>()
    const second = deferred<{ groups: unknown[] }>()
    const searchRemote = vi.fn()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
      .mockRejectedValueOnce(new Error('Remote exploded'))
      .mockRejectedValueOnce(new Error('   '))
    mocks.config.searchRemote = searchRemote

    const store = useCommandPaletteStore()
    store.open()
    store.setQuery('a')
    await flushMicrotasks()
    expect(store.remoteLoading).toBe(false)

    store.setQuery('> command')
    store.setQuery('/ page')
    store.setQuery('# report')
    await flushMicrotasks()
    expect(searchRemote).not.toHaveBeenCalled()

    store.setQuery(': first')
    await vi.advanceTimersByTimeAsync(160)
    expect(searchRemote).toHaveBeenCalledTimes(1)

    store.setQuery('@ second')
    await vi.advanceTimersByTimeAsync(160)
    expect(searchRemote).toHaveBeenCalledTimes(2)

    first.resolve({ groups: [] })
    await flushMicrotasks()
    expect(store.remoteLoading).toBe(true)

    second.resolve({
      groups: [
        {
          code: 'catalogs',
          label: 'Catalogs',
          items: [
            {
              key: 'remote:catalog',
              kind: 'catalog',
              title: 'Catalog item',
              openInNewTabSupported: true,
            },
            {
              key: 'remote:report-kind',
              kind: 'report',
              title: 'Report-like item',
              subtitle: 'Remote',
              icon: 'book',
              badge: 'Report',
              route: '/remote/report',
              commandCode: 'open-report',
              status: 'Ready',
              openInNewTabSupported: true,
              score: 0.9,
            },
            {
              key: 'remote:page-kind',
              kind: 'page',
              title: 'Page-like item',
              icon: 'folder',
              openInNewTabSupported: true,
              score: 0.9,
            },
            {
              key: 'remote:document-kind',
              kind: 'document',
              title: 'Document-like item',
              icon: 'file',
              openInNewTabSupported: true,
              score: 0.9,
            },
          ],
        },
        {
          code: 'reports',
          label: 'Reports',
          items: [{ key: 'filtered:report', kind: 'report', title: 'Filtered', openInNewTabSupported: true, score: 1 }],
        },
      ],
    })
    await flushMicrotasks()

    expect(store.groups.some((group) => group.code === 'catalogs')).toBe(true)
    expect(store.flatItems.map((item) => item.scope)).toEqual(expect.arrayContaining(['catalogs', 'reports', 'pages', 'documents']))

    store.setQuery(': error')
    await vi.advanceTimersByTimeAsync(160)
    await flushMicrotasks()
    expect(store.remoteError).toBe('Remote exploded')
    expect(store.hasRemoteError).toBe(true)

    store.setQuery(': fallback')
    await vi.advanceTimersByTimeAsync(160)
    await flushMicrotasks()
    expect(store.remoteError).toBe('Could not update remote results.')
    expect(store.remoteLoading).toBe(false)
  })

  it('ignores an aborted remote request and sends a complete GUID context', async () => {
    vi.useFakeTimers()
    const pending = deferred<{ groups: unknown[] }>()
    const searchRemote = vi.fn()
      .mockReturnValueOnce(pending.promise)
      .mockResolvedValueOnce({ groups: [] })
    mocks.config.searchRemote = searchRemote

    const store = useCommandPaletteStore()
    store.setExplicitContext('guid-context', {
      entityId: '12345678-1234-1234-1234-123456789abc',
      actions: [],
    })
    store.open()
    store.setQuery(': invoice')
    await vi.advanceTimersByTimeAsync(160)

    expect(searchRemote).toHaveBeenCalledWith(expect.objectContaining({
      context: {
        entityType: null,
        documentType: null,
        catalogType: null,
        entityId: '12345678-1234-1234-1234-123456789abc',
      },
    }), expect.any(AbortSignal))

    store.setQuery('a')
    pending.reject(new Error('Canceled request must be ignored'))
    await flushMicrotasks()
    expect(store.remoteError).toBeNull()

    store.setExplicitContext('guid-context', { actions: [] })
    store.setQuery(': another')
    await vi.advanceTimersByTimeAsync(160)
    await flushMicrotasks()
    expect(searchRemote.mock.calls[1]?.[0]).toEqual(expect.objectContaining({
      context: {
        entityType: null,
        documentType: null,
        catalogType: null,
        entityId: null,
      },
    }))
  })

  it('filters inaccessible recent entries and materializes optional fields for an allowed descendant route', async () => {
    mocks.loadRecent.mockReturnValue([
      {
        key: 'recent:missing-route',
        kind: 'recent',
        scope: 'pages',
        title: 'Missing route',
        route: null,
        timestamp: '2026-04-08T12:00:00.000Z',
      },
      {
        key: 'recent:home-child',
        kind: 'recent',
        scope: 'reports',
        title: 'Home child',
        route: '/home/child?tab=one',
        timestamp: '2026-04-08T12:00:00.000Z',
      },
    ])

    const store = useCommandPaletteStore()
    store.open()
    await flushMicrotasks()

    const recent = store.flatItems.find((item) => item.key === 'recent:recent:home-child')
    expect(recent).toMatchObject({
      subtitle: null,
      icon: 'bar-chart',
      badge: null,
      route: '/home/child?tab=one',
      status: null,
    })
    expect(store.flatItems.some((item) => item.title === 'Missing route')).toBe(false)
  })

  it('normalizes legacy, native, missing, and unknown icons while deduping seeds and sorting ties', async () => {
    const legacyIcons = ['chart', 'file', 'book', 'folder', 'unknown-icon', null] as const
    mocks.config.buildHeuristicCurrentActions = vi.fn().mockReturnValue([
      makeItem({ key: 'duplicate-seed', title: 'Duplicate first', icon: 'search', defaultRank: 0 }),
      makeItem({ key: 'duplicate-seed', title: 'Duplicate second', icon: 'search', defaultRank: 0 }),
      makeItem({ key: 'cross-source-duplicate', title: 'Cross-source first', icon: 'search', defaultRank: 400 }),
    ])
    mocks.config.favoriteItems = [
      makeItem({ key: 'cross-source-duplicate', title: 'Cross-source second', defaultRank: 400 }),
      ...legacyIcons.map((icon, index) => makeItem({
        key: `icon:${String(icon)}`,
        title: `Same target ${index}`,
        icon,
        subtitle: undefined,
        badge: undefined,
        keywords: undefined,
        defaultRank: 400,
      })),
    ]
    mocks.config.createItems = [makeItem({
      key: 'route-less-create',
      title: 'Same target create',
      route: null,
      defaultRank: 400,
    })]
    mocks.menuStore.groups.push({
      label: 'Catalogs',
      ordinal: 20,
      icon: 'grid',
      items: [
        { kind: 'page', code: 'catalog-page', label: 'Catalog page', route: '/catalogs/custom', icon: null, ordinal: 0 },
      ],
    })

    const store = useCommandPaletteStore()
    store.open()
    await flushMicrotasks()
    expect(store.flatItems.filter((item) => item.key === 'cross-source-duplicate')).toHaveLength(1)
    store.setQuery('same target')

    const items = store.flatItems.filter((item) => item.title.startsWith('Same target'))
    expect(items.map((item) => item.icon)).toEqual(expect.arrayContaining([
      'bar-chart',
      'file-text',
      'book-open',
      'grid',
      'file-text',
    ]))
    store.setActiveIndex(0)
    await store.executeActive()
  })
})
