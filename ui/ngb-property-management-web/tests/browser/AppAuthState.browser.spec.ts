import { reactive } from 'vue'
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
      render: () => null,
    },
    NgbSiteShell: {
      name: 'NgbSiteShell',
      props: ['nodes', 'selectedId'],
      setup(props: { nodes?: SiteNodeStub[]; selectedId?: string | null }) {
        return () => h('div', { 'data-testid': 'pm-shell' }, [
          h('div', { 'data-testid': 'pm-shell-selected-id' }, props.selectedId ?? ''),
          h('nav', { 'data-testid': 'pm-shell-nav' }, (props.nodes ?? []).flatMap((node) => [
            h('div', { 'data-testid': 'pm-shell-node' }, node.label),
            ...(node.children ?? []).map((child) => h('a', { href: child.route ?? '#', 'data-testid': 'pm-shell-node' }, child.label)),
          ])),
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
          render: () => null,
        },
      },
    },
  })
}

beforeEach(() => {
  mocks.routerPush.mockReset()
  mocks.route = reactive({
    fullPath: '/reports/accounting.posting_log?periodFrom=2026-01&periodTo=2026-04',
    path: '/reports/accounting.posting_log',
    matched: [],
  }) as RouteState
  mocks.menuStore = {
    groups: [],
    load: vi.fn(async () => undefined),
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
  }

  const view = await renderApp()

  await expect.element(view.getByTestId('pm-shell-nav')).toHaveTextContent('Journal Entries')
  await expect.element(view.getByTestId('pm-shell-selected-id'))
    .toHaveTextContent('document:general_journal_entry')
})
