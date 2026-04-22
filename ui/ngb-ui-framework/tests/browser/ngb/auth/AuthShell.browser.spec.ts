import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'

import type { AuthSnapshot } from '../../../../src/ngb/auth/types'

const authShellMocks = vi.hoisted(() => {
  const state = {
    snapshot: {
      initialized: false,
      authenticated: false,
      token: null,
      subject: null,
      displayName: null,
      preferredUsername: null,
      email: null,
      realmRoles: [],
      resourceRoles: {},
      roles: [],
    } as AuthSnapshot,
    subscriber: null as null | ((snapshot: AuthSnapshot) => void),
  }

  return {
    state,
    getAuthSnapshot: vi.fn(() => state.snapshot),
    initializeAuth: vi.fn(),
    loginWithKeycloak: vi.fn(),
    logoutFromKeycloak: vi.fn(),
    subscribeAuth: vi.fn((listener: (snapshot: AuthSnapshot) => void) => {
      state.subscriber = listener
    }),
  }
})

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAuthSnapshot: authShellMocks.getAuthSnapshot,
  initializeAuth: authShellMocks.initializeAuth,
  loginWithKeycloak: authShellMocks.loginWithKeycloak,
  logoutFromKeycloak: authShellMocks.logoutFromKeycloak,
  subscribeAuth: authShellMocks.subscribeAuth,
}))

import { createAuthGuard } from '../../../../src/ngb/auth/router'
import { useAuthStore } from '../../../../src/ngb/auth/useAuthStore'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

function createSnapshot(overrides: Partial<AuthSnapshot> = {}): AuthSnapshot {
  return {
    initialized: false,
    authenticated: false,
    token: null,
    subject: null,
    displayName: null,
    preferredUsername: null,
    email: null,
    realmRoles: [],
    resourceRoles: {},
    roles: [],
    ...overrides,
  }
}

function isVisible(element: Element): boolean {
  let current: Element | null = element

  while (current) {
    const style = window.getComputedStyle(current as HTMLElement)
    if (style.display === 'none' || style.visibility === 'hidden') return false
    current = current.parentElement
  }

  return true
}

function visibleButtonByTitle(title: string): HTMLElement {
  const button = Array.from(document.querySelectorAll(`button[title="${title}"]`)).find(isVisible) as HTMLElement | undefined
  if (!button) throw new Error(`Visible button not found for title: ${title}`)
  return button
}

function visibleUserButton(): HTMLElement {
  return visibleButtonByTitle('User')
}

function visibleButtonContainingText(text: string): HTMLElement {
  const button = Array.from(document.querySelectorAll('button')).find((entry) => (
    isVisible(entry) && entry.textContent?.includes(text)
  )) as HTMLElement | undefined

  if (!button) throw new Error(`Visible button not found containing text: ${text}`)
  return button
}

const shellNodes = [
  { id: 'dashboard', label: 'Dashboard', route: '/dashboard', icon: 'home' },
]

const shellSettings = [
  {
    label: 'Workspace',
    items: [
      { label: 'Preferences', route: '/settings/preferences', icon: 'settings' },
    ],
  },
]

const PublicPage = {
  render: () => h('div', { 'data-testid': 'auth-public-page' }, 'Public page'),
}

const ProtectedShellPage = defineComponent({
  setup() {
    const auth = useAuthStore()

    return () => h('div', [
      h('div', { 'data-testid': 'auth-shell-error' }, auth.error ?? ''),
      h('div', { 'data-testid': 'auth-shell-redirecting' }, String(auth.redirecting)),
      h(
        NgbSiteShell,
        {
          moduleTitle: 'Property Management',
          productTitle: 'NGB',
          userName: auth.userName,
          userEmail: auth.email ?? '',
          userMeta: auth.primaryRoleLabel,
          userMetaIcon: auth.primaryRoleIcon,
          pinned: [],
          recent: [],
          nodes: shellNodes,
          settings: shellSettings,
          selectedId: 'dashboard',
          onSignOut: () => {
            void auth.logout().catch(() => undefined)
          },
        },
        {
          default: () => h('div', { 'data-testid': 'auth-shell-page', class: 'flex-1 min-h-0 overflow-auto p-4' }, 'Protected workspace'),
        },
      ),
    ])
  },
})

const AppRoot = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'auth-shell-current-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

async function renderAuthShellHarness(initialRoute = '/public') {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/public',
        meta: { public: true },
        component: PublicPage,
      },
      {
        path: '/app',
        component: ProtectedShellPage,
      },
    ],
  })

  router.beforeEach(createAuthGuard(() => auth))
  await router.push(initialRoute)
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [pinia, router],
    },
  })

  return {
    auth,
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  authShellMocks.state.snapshot = createSnapshot()
  authShellMocks.state.subscriber = null
})

test('boots the protected shell through auth initialization and hydrates shell identity', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: true,
    displayName: 'Alex Carter',
    email: 'alex.carter@example.com',
    roles: ['realm-admin'],
  }))

  const { view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  expect(authShellMocks.initializeAuth).toHaveBeenCalledTimes(1)
  expect(authShellMocks.loginWithKeycloak).not.toHaveBeenCalled()

  await visibleUserButton().click()
  await expect.element(view.getByText('Alex Carter', { exact: true })).toBeVisible()
  await expect.element(view.getByText('alex.carter@example.com', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Administrator', { exact: true })).toBeVisible()
})

test('keeps the protected shell mounted and exposes auth initialization errors to the app UI', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockRejectedValueOnce(new Error('Keycloak init failed'))

  const { view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await expect.element(view.getByTestId('auth-shell-error')).toHaveTextContent('Keycloak init failed')
  expect(authShellMocks.loginWithKeycloak).not.toHaveBeenCalled()
})

test('recovers the protected shell on a later navigation after the first auth initialization attempt fails', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth
    .mockRejectedValueOnce(new Error('Keycloak init failed'))
    .mockResolvedValueOnce(createSnapshot({
      initialized: true,
      authenticated: true,
      displayName: 'Recovered User',
      email: 'recovered.user@example.com',
      roles: ['realm-admin'],
    }))

  const { router, view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await expect.element(view.getByTestId('auth-shell-error')).toHaveTextContent('Keycloak init failed')

  await router.push('/public')
  await expect.element(view.getByTestId('auth-public-page')).toBeVisible()

  await router.push('/app?tab=recovered')
  await expect.element(view.getByTestId('auth-shell-current-route')).toHaveTextContent('/app?tab=recovered')
  await expect.element(view.getByTestId('auth-shell-error')).toHaveTextContent('')
  expect(authShellMocks.initializeAuth).toHaveBeenCalledTimes(2)

  await visibleUserButton().click()
  await expect.element(view.getByText('Recovered User', { exact: true })).toBeVisible()
  await expect.element(view.getByText('recovered.user@example.com', { exact: true })).toBeVisible()
})

test('starts login for protected shell navigation and keeps the public route stable while redirecting', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: false,
  }))

  const { router, view } = await renderAuthShellHarness('/public')
  await router.push('/app?tab=details')

  expect(authShellMocks.initializeAuth).toHaveBeenCalledTimes(1)
  expect(authShellMocks.loginWithKeycloak).toHaveBeenCalledWith('/app?tab=details')
  await expect.element(view.getByTestId('auth-public-page')).toBeVisible()
  await expect.element(view.getByTestId('auth-shell-current-route')).toHaveTextContent('/public')
})

test('keeps the protected shell mounted while refreshed auth snapshots update identity in place', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: true,
    token: 'stale-token',
    displayName: 'Alex Carter',
    email: 'alex.carter@example.com',
    roles: ['realm-admin'],
  }))

  const { router, view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()

  authShellMocks.state.subscriber?.(createSnapshot({
    initialized: true,
    authenticated: true,
    token: 'fresh-token',
    displayName: 'Alex Refresh',
    email: 'alex.refresh@example.com',
    roles: ['realm-admin'],
  }))

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await vi.waitFor(() => {
    expect(view.getByTestId('auth-shell-current-route').element().textContent).toBe('/app')
  })

  await router.push('/app?tab=details')
  await expect.element(view.getByTestId('auth-shell-current-route')).toHaveTextContent('/app?tab=details')
  expect(authShellMocks.loginWithKeycloak).not.toHaveBeenCalled()

  await visibleUserButton().click()
  await expect.element(view.getByText('Alex Refresh', { exact: true })).toBeVisible()
  await expect.element(view.getByText('alex.refresh@example.com', { exact: true })).toBeVisible()
})

test('preserves the intended protected route when a refreshed session drops to unauthenticated state', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: true,
    token: 'active-token',
    displayName: 'Alex Carter',
    email: 'alex.carter@example.com',
    roles: ['realm-admin'],
  }))
  authShellMocks.loginWithKeycloak.mockResolvedValueOnce(undefined)

  const { router, view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()

  authShellMocks.state.subscriber?.(createSnapshot({
    initialized: true,
    authenticated: false,
    token: null,
    displayName: null,
    email: null,
  }))

  await router.push('/app?tab=renewed')

  expect(authShellMocks.loginWithKeycloak).toHaveBeenCalledWith('/app?tab=renewed')
  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await expect.element(view.getByTestId('auth-shell-current-route')).toHaveTextContent('/app')
  await expect.element(view.getByTestId('auth-shell-redirecting')).toHaveTextContent('true')
})

test('forwards shell sign out into the auth store and leaves the redirect state visible during logout', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: true,
    displayName: 'Alex Carter',
    email: 'alex.carter@example.com',
    roles: ['realm-admin'],
  }))
  authShellMocks.logoutFromKeycloak.mockResolvedValueOnce(undefined)

  const { view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await visibleUserButton().click()
  await visibleButtonContainingText('Sign out').click()

  expect(authShellMocks.logoutFromKeycloak).toHaveBeenCalledTimes(1)
  await expect.poll(() => view.getByTestId('auth-shell-redirecting').element().textContent ?? '').toBe('true')
  expect(view.getByTestId('auth-shell-error').element().textContent).toBe('')
})

test('surfaces logout failures back through the protected shell UI', async () => {
  await page.viewport(1440, 900)

  authShellMocks.initializeAuth.mockResolvedValueOnce(createSnapshot({
    initialized: true,
    authenticated: true,
    displayName: 'Alex Carter',
    email: 'alex.carter@example.com',
    roles: ['realm-admin'],
  }))
  authShellMocks.logoutFromKeycloak.mockRejectedValueOnce(new Error('Logout blocked'))

  const { view } = await renderAuthShellHarness('/app')

  await expect.element(view.getByTestId('auth-shell-page')).toBeVisible()
  await visibleUserButton().click()
  await visibleButtonContainingText('Sign out').click()

  expect(authShellMocks.logoutFromKeycloak).toHaveBeenCalledTimes(1)
  await expect.poll(() => view.getByTestId('auth-shell-redirecting').element().textContent ?? '').toBe('false')
  await expect.element(view.getByTestId('auth-shell-error')).toHaveTextContent('Logout blocked')
})
