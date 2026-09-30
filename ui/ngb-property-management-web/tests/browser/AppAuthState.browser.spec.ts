import { h, reactive } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  authStore: null as AuthStore | null,
  accessStore: null as AccessStore | null,
  menuStore: null as MenuStore | null,
  paletteStore: null as PaletteStore | null,
  route: null as RouteState | null,
  routerPush: vi.fn(),
}))

vi.mock('vue-router', async () => {
  const actual = await vi.importActual<typeof import('vue-router')>('vue-router')

  return {
    ...actual,
    useRouter: () => ({ push: mocks.routerPush }),
    useRoute: () => mocks.route,
  }
})

vi.mock('@ngbplatform/ui', async () => {
  const { h } = await vi.importActual<typeof import('vue')>('vue')

  function normalizeNgbRouteAliasPath(value: string | null | undefined): string {
    const path = String(value ?? '').trim()
    if (path.startsWith('/documents/accounting.general_journal_entry')) {
      return path.replace('/documents/accounting.general_journal_entry', '/accounting/general-journal-entries')
    }
    if (path.startsWith('/documents/general_journal_entry')) {
      return path.replace('/documents/general_journal_entry', '/accounting/general-journal-entries')
    }
    if (path === '/admin/accounting/posting-log') return '/reports/accounting.posting_log'
    if (path === '/admin/accounting/consistency') return '/reports/accounting.consistency'
    return path
  }

  return {
    NgbCommandPaletteDialog: {
      name: 'NgbCommandPaletteDialog',
      render: () => h('div', { 'data-testid': 'pm-palette-dialog' }),
    },
    NgbSiteShell: {
      name: 'NgbSiteShell',
      props: ['nodes', 'selectedId', 'userMeta', 'userEmail'],
      emits: ['navigate', 'select', 'openPalette', 'signOut'],
      setup(
        props: { nodes?: SiteNodeStub[]; selectedId?: string | null; userMeta?: string; userEmail?: string },
        { emit, slots }: { emit: (event: string, ...args: unknown[]) => void; slots: Record<string, () => unknown> },
      ) {
        return () => h('div', { 'data-testid': 'pm-shell' }, [
          h('div', { 'data-testid': 'pm-shell-selected-id' }, props.selectedId ?? ''),
          h('div', { 'data-testid': 'pm-shell-user-meta' }, props.userMeta ?? ''),
          h('div', { 'data-testid': 'pm-shell-user-email' }, props.userEmail ?? ''),
          h('nav', { 'data-testid': 'pm-shell-nav' }, (props.nodes ?? []).flatMap((node) => [
            h('div', { 'data-testid': 'pm-shell-node' }, node.label),
            ...(node.children ?? []).map((child) => h('a', { href: child.route ?? '#', 'data-testid': 'pm-shell-node' }, child.label)),
          ])),
          h('button', { type: 'button', onClick: () => emit('navigate', '') }, 'Navigate empty'),
          h('button', { type: 'button', onClick: () => emit('navigate', null) }, 'Navigate null'),
          h('button', { type: 'button', onClick: () => emit('navigate', '/properties') }, 'Navigate internal'),
          h('button', { type: 'button', onClick: () => emit('navigate', 'https://status.example/pm') }, 'Navigate external'),
          h('button', { type: 'button', onClick: () => emit('select', 'child', '/leases') }, 'Select route'),
          h('button', { type: 'button', onClick: () => emit('openPalette') }, 'Open palette'),
          h('button', { type: 'button', onClick: () => emit('signOut') }, 'Sign out'),
          slots.default?.(),
        ])
      },
    },
    normalizeNgbRouteAliasPath,
    useAuthStore: () => mocks.authStore,
    useAccessStore: () => mocks.accessStore,
    useCommandPaletteHotkeys: () => undefined,
    useCommandPaletteStore: () => mocks.paletteStore,
    useMainMenuStore: () => mocks.menuStore,
  }
})

import App from '../../src/App.vue'

type AuthStore = {
  initialized: boolean
  initializing: boolean
  redirecting: boolean
  authenticated: boolean
  userName: string
  email: string
  primaryRoleLabel: string
  primaryRoleIcon: 'shield-check' | 'user'
  error: string | null
  login: ReturnType<typeof vi.fn>
  initialize: ReturnType<typeof vi.fn>
  logout: ReturnType<typeof vi.fn>
}

type AccessStore = {
  current: {
    isBootstrapAdmin: boolean
  } | null
  applicationRoleNames: string[]
  load: ReturnType<typeof vi.fn>
  reset: ReturnType<typeof vi.fn>
}

type MenuStore = {
  groups: unknown[]
  load: ReturnType<typeof vi.fn>
  reset: ReturnType<typeof vi.fn>
}

type SiteNodeStub = {
  label: string
  route?: string | null
  children?: SiteNodeStub[]
}

type PaletteStore = {
  hydrate: ReturnType<typeof vi.fn>
  open: ReturnType<typeof vi.fn>
  setCurrentRoute: ReturnType<typeof vi.fn>
}

type RouteState = {
  fullPath: string
  path: string
  matched: Array<{ meta?: Record<string, unknown> }>
}

function createAccessStore(overrides: Partial<AccessStore> = {}): AccessStore {
  return reactive({
    current: {
      isBootstrapAdmin: false,
    },
    applicationRoleNames: ['PM Administrator'],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
    ...overrides,
  }) as AccessStore
}

function createAuthStore(overrides: Partial<AuthStore> = {}): AuthStore {
  return reactive({
    initialized: false,
    initializing: false,
    redirecting: false,
    authenticated: false,
    userName: 'UI Tester',
    email: 'ui.tester@demo.ngbplatform.com',
    primaryRoleLabel: 'Administrator',
    primaryRoleIcon: 'shield-check' as const,
    error: null,
    login: vi.fn(async () => undefined),
    initialize: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
    ...overrides,
  }) as AuthStore
}

async function renderApp() {
  return await render(App, {
    global: {
      stubs: {
        RouterView: {
          name: 'RouterView',
          render: () => h('div', { 'data-testid': 'pm-router-view' }),
        },
      },
    },
  })
}

beforeEach(() => {
  vi.restoreAllMocks()
  mocks.routerPush.mockReset()
  mocks.route = reactive({
    fullPath: '/reports/accounting.posting_log?periodFrom=2026-01&periodTo=2026-04',
    path: '/reports/accounting.posting_log',
    matched: [],
  }) as RouteState
  mocks.menuStore = {
    groups: [],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }
  mocks.accessStore = createAccessStore()
  mocks.paletteStore = {
    hydrate: vi.fn(async () => undefined),
    open: vi.fn(),
    setCurrentRoute: vi.fn(),
  }
  mocks.authStore = createAuthStore()
})

test('renders the initializing auth state while Keycloak session detection is in flight', async () => {
  mocks.authStore = createAuthStore({
    initializing: true,
  })

  const view = await renderApp()

  await expect.element(view.getByText('Connecting to Keycloak', { exact: true })).toBeVisible()
  await expect.element(view.getByText(
    'Checking whether an existing SSO session is already available.',
    { exact: true },
  )).toBeVisible()

  expect(document.body.textContent ?? '').not.toContain('Retry')
  expect(document.body.textContent ?? '').not.toContain('Sign in')
})

test('renders the redirecting auth state while the secure sign-in handoff is active', async () => {
  mocks.authStore = createAuthStore({
    redirecting: true,
  })

  const view = await renderApp()

  await expect.element(view.getByText('Redirecting to secure sign-in', { exact: true })).toBeVisible()
  await expect.element(view.getByText(
    'You will be sent to the login page in a moment.',
    { exact: true },
  )).toBeVisible()

  expect(document.body.textContent ?? '').not.toContain('Retry')
  expect(document.body.textContent ?? '').not.toContain('Sign in')
})

test('retries authentication from the blocking error state and preserves the current route', async () => {
  const login = vi.fn(async () => undefined)
  const initialize = vi.fn(async () => {
    if (!mocks.authStore) return
    mocks.authStore.error = null
    mocks.authStore.initialized = true
  })

  mocks.authStore = createAuthStore({
    error: 'Keycloak did not respond.',
    initialize,
    login,
  })

  const view = await renderApp()

  await expect.element(view.getByText('Unable to start the secure session', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Retry' }).click()

  await expect.poll(() => initialize.mock.calls.length).toBe(1)
  await expect.poll(() => login.mock.calls.length).toBe(1)
  expect(login).toHaveBeenCalledWith('/reports/accounting.posting_log?periodFrom=2026-01&periodTo=2026-04')
})

test('starts a direct sign-in from the blocking error state', async () => {
  const login = vi.fn(async () => undefined)

  mocks.authStore = createAuthStore({
    error: 'Keycloak did not respond.',
    login,
  })

  const view = await renderApp()

  await view.getByRole('button', { name: 'Sign in' }).click()

  await expect.poll(() => login.mock.calls.length).toBe(1)
  expect(login).toHaveBeenCalledWith('/reports/accounting.posting_log?periodFrom=2026-01&periodTo=2026-04')
})

test('renders Posting Log from the permission-filtered backend menu without hidden admin neighbors', async () => {
  mocks.authStore = createAuthStore({
    authenticated: true,
  })
  mocks.menuStore = {
    groups: [
      {
        label: 'Accounting',
        ordinal: 50,
        icon: 'calculator',
        items: [
          {
            kind: 'report',
            code: 'accounting.balance_sheet',
            label: 'Balance Sheet',
            route: '/reports/accounting.balance_sheet',
            icon: 'bar-chart',
            ordinal: 20,
          },
        ],
      },
      {
        label: 'Setup & Controls',
        ordinal: 70,
        icon: 'settings',
        items: [
          {
            kind: 'admin',
            code: 'accounting.posting_log',
            label: 'Posting Log',
            route: '/admin/accounting/posting-log',
            icon: 'history',
            ordinal: 70,
          },
          {
            kind: 'external',
            code: 'pm.health',
            label: 'Health',
            route: 'https://localhost:7075/health-ui',
            icon: 'heart-pulse',
            ordinal: 90,
          },
          {
            kind: 'external',
            code: 'pm.background_jobs',
            label: 'Background Jobs',
            route: 'https://localhost:7074/hangfire',
            icon: 'cogs',
            ordinal: 100,
          },
        ],
      },
    ],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }

  const view = await renderApp()

  await expect.poll(() => mocks.menuStore?.load.mock.calls.length ?? 0).toBe(1)
  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Balance Sheet')
  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Posting Log')
  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Health')
  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Background Jobs')
  await expect.element(view.getByTestId('pm-shell-nav')).not.toHaveTextContent('Period Close')
  await expect.element(view.getByTestId('pm-shell-nav')).not.toHaveTextContent('Integrity Checks')
  await expect.element(view.getByTestId('pm-shell-selected-id')).toHaveTextContent('admin:accounting.posting_log')
})

test('renders and selects Journal Entries as a document-backed menu item', async () => {
  mocks.authStore = createAuthStore({
    authenticated: true,
  })
  mocks.route = reactive({
    fullPath: '/accounting/general-journal-entries',
    path: '/accounting/general-journal-entries',
    matched: [],
  }) as RouteState
  mocks.menuStore = {
    groups: [
      {
        label: 'Accounting',
        ordinal: 60,
        icon: 'calculator',
        items: [
          {
            kind: 'document',
            code: 'general_journal_entry',
            label: 'Journal Entries',
            route: '/accounting/general-journal-entries',
            icon: 'book-open',
            ordinal: 10,
          },
        ],
      },
    ],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }

  const view = await renderApp()

  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Journal Entries')
  await expect.element(view.getByTestId('pm-shell-selected-id'))
    .toHaveTextContent('document:general_journal_entry')
})

test('handles shell actions, alias navigation, sign-out, and palette opening', async () => {
  const logout = vi.fn(async () => undefined)
  mocks.authStore = createAuthStore({ authenticated: true, logout })
  mocks.accessStore = createAccessStore({ applicationRoleNames: [], current: null })
  const open = vi.spyOn(window, 'open').mockImplementation(() => null)

  const view = await renderApp()

  await view.getByRole('button', { name: 'Navigate empty' }).click()
  await view.getByRole('button', { name: 'Navigate null' }).click()
  expect(mocks.routerPush).not.toHaveBeenCalled()

  await view.getByRole('button', { name: 'Navigate internal' }).click()
  await view.getByRole('button', { name: 'Select route' }).click()
  await view.getByRole('button', { name: 'Navigate external' }).click()
  expect(mocks.routerPush).toHaveBeenNthCalledWith(1, '/properties')
  expect(mocks.routerPush).toHaveBeenNthCalledWith(2, '/leases')
  expect(open).toHaveBeenCalledWith('https://status.example/pm', '_self')
  await expect.element(view.getByTestId('pm-shell-user-meta')).toHaveTextContent('')

  await view.getByRole('button', { name: 'Open palette' }).click()
  expect(mocks.paletteStore?.open).toHaveBeenCalledOnce()

  await view.getByRole('button', { name: 'Sign out' }).click()
  expect(logout).toHaveBeenCalledOnce()
  expect(view.getByTestId('pm-palette-dialog').element()).not.toBeNull()
  expect(view.getByTestId('pm-router-view').element()).not.toBeNull()
})

test('builds a root menu leaf, falls back to a stable group id, and marks descendant routes selected', async () => {
  mocks.authStore = createAuthStore({ authenticated: true, email: null as unknown as string })
  mocks.accessStore = createAccessStore({
    applicationRoleNames: [],
    current: { isBootstrapAdmin: true },
  })
  mocks.route = reactive({
    fullPath: '/properties/active?view=cards',
    path: '/properties/active',
    matched: [{}],
  }) as RouteState
  mocks.menuStore = {
    groups: [
      {
        label: '!!!',
        ordinal: 20,
        icon: null,
        items: [{ kind: 'catalog', code: 'properties', label: '!!!', route: '/properties', icon: 'building', ordinal: 1 }],
      },
      {
        label: 'Empty route',
        ordinal: 10,
        icon: null,
        items: [{ kind: 'catalog', code: 'empty', label: 'Empty route', route: null, icon: null, ordinal: 1 }],
      },
      {
        label: 'No icons',
        ordinal: 30,
        icon: null,
        items: [
          { kind: 'catalog', code: 'first', label: 'First', route: '/first', icon: null, ordinal: 2 },
          { kind: 'catalog', code: 'second', label: 'Second', route: '/second', icon: 'star', ordinal: 1 },
        ],
      },
    ],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }

  const view = await renderApp()

  await expect.element(view.getByTestId('pm-shell-selected-id')).toHaveTextContent('group:menu')
  await expect.element(view.getByTestId('pm-shell-user-meta')).toHaveTextContent('Bootstrap admin')
  await expect.element(view.getByTestId('pm-shell-user-email')).toHaveTextContent('')
  expect(Array.from(document.querySelectorAll('[data-testid="pm-shell-node"]'), (element) => element.textContent)).toEqual([
    'Empty route',
    '!!!',
    'No icons',
    'Second',
    'First',
  ])
})

test('renders an empty shell navigation when the menu response has no groups', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  mocks.menuStore = {
    groups: null as unknown as unknown[],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }

  const view = await renderApp()

  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('')
  expect(document.querySelectorAll('[data-testid="pm-shell-node"]')).toHaveLength(0)
})

test('renders a bare route without the site shell or command palette', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  mocks.route = reactive({
    fullPath: '/public/print',
    path: '/public/print',
    matched: [{ meta: { bare: true } }],
  }) as RouteState

  const view = await renderApp()

  expect(view.getByTestId('pm-router-view').element()).not.toBeNull()
  expect(document.querySelector('[data-testid="pm-shell"]')).toBeNull()
  expect(document.querySelector('[data-testid="pm-palette-dialog"]')).toBeNull()
})

test('does not start login after retry when authentication succeeds, remains failed, or initialization rejects', async () => {
  const login = vi.fn(async () => undefined)

  const authenticatedInitialize = vi.fn(async () => {
    mocks.authStore!.authenticated = true
    mocks.authStore!.error = null
  })
  mocks.authStore = createAuthStore({ error: 'Retry required', initialize: authenticatedInitialize, login })
  const authenticated = await renderApp()
  await authenticated.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => authenticatedInitialize.mock.calls.length).toBe(1)
  expect(login).not.toHaveBeenCalled()
  authenticated.unmount()

  const stillFailedInitialize = vi.fn(async () => undefined)
  mocks.authStore = createAuthStore({ error: 'Still failed', initialize: stillFailedInitialize, login })
  const stillFailed = await renderApp()
  await stillFailed.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => stillFailedInitialize.mock.calls.length).toBe(1)
  expect(login).not.toHaveBeenCalled()
  stillFailed.unmount()

  const rejectedInitialize = vi.fn(async () => { throw new Error('identity offline') })
  mocks.authStore = createAuthStore({ error: 'Offline', initialize: rejectedInitialize, login })
  const rejected = await renderApp()
  await rejected.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => rejectedInitialize.mock.calls.length).toBe(1)
  expect(login).not.toHaveBeenCalled()
})

test('resets and rehydrates dependent stores when authentication changes', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  const view = await renderApp()
  await expect.poll(() => mocks.accessStore?.load.mock.calls.length ?? 0).toBe(1)

  mocks.authStore.authenticated = false
  await expect.poll(() => mocks.accessStore?.reset.mock.calls.length ?? 0).toBe(1)

  mocks.authStore.authenticated = true
  await expect.poll(() => mocks.accessStore?.load.mock.calls.length ?? 0).toBe(2)
  expect(mocks.menuStore?.load).toHaveBeenCalledTimes(2)
  expect(mocks.paletteStore?.hydrate).toHaveBeenCalledTimes(2)
  expect(view.getByTestId('pm-shell').element()).not.toBeNull()
})
