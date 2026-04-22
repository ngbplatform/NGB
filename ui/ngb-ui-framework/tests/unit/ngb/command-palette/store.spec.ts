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

vi.mock('../../../../src/ngb/site/mainMenuStore', () => ({
  useMainMenuStore: () => mocks.menuStore,
}))

vi.mock('../../../../src/ngb/command-palette/storage', () => ({
  loadCommandPaletteRecent: mocks.loadRecent,
  saveCommandPaletteRecent: mocks.saveRecent,
}))

import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'

async function flushMicrotasks() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('command palette store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useRealTimers()
    setActivePinia(createPinia())

    mocks.config.router.currentRoute.value.fullPath = '/home'
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
})
