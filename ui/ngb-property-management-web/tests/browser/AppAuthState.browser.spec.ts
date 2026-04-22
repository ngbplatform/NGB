import { reactive } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  authStore: null as AuthStore | null,
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

vi.mock('ngb-ui-framework', () => ({
  NgbCommandPaletteDialog: {
    name: 'NgbCommandPaletteDialog',
    render: () => null,
  },
  NgbSiteShell: {
    name: 'NgbSiteShell',
    render: () => null,
  },
  normalizeNgbRouteAliasPath: (value: string | null | undefined) => String(value ?? '').trim(),
  useAuthStore: () => mocks.authStore,
  useCommandPaletteHotkeys: () => undefined,
  useCommandPaletteStore: () => mocks.paletteStore,
  useMainMenuStore: () => mocks.menuStore,
}))

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

type MenuStore = {
  groups: unknown[]
  load: ReturnType<typeof vi.fn>
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
