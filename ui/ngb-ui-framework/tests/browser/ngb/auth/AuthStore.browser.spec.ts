import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h } from 'vue'

import type { AuthSnapshot } from '../../../../src/ngb/auth/types'

const authMocks = vi.hoisted(() => {
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
  getAuthSnapshot: authMocks.getAuthSnapshot,
  initializeAuth: authMocks.initializeAuth,
  loginWithKeycloak: authMocks.loginWithKeycloak,
  logoutFromKeycloak: authMocks.logoutFromKeycloak,
  subscribeAuth: authMocks.subscribeAuth,
}))

import { useAuthStore } from '../../../../src/ngb/auth/useAuthStore'

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

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

const AuthStoreHarness = defineComponent({
  setup() {
    const auth = useAuthStore()

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          void auth.initialize().catch(() => undefined)
        },
      }, 'Initialize auth'),
      h('button', {
        type: 'button',
        onClick: () => {
          void auth.login('/reports/occupancy').catch(() => undefined)
        },
      }, 'Login auth'),
      h('button', {
        type: 'button',
        onClick: () => {
          void auth.logout().catch(() => undefined)
        },
      }, 'Logout auth'),
      h('div', { 'data-testid': 'auth-initialized' }, String(auth.initialized)),
      h('div', { 'data-testid': 'auth-initializing' }, String(auth.initializing)),
      h('div', { 'data-testid': 'auth-redirecting' }, String(auth.redirecting)),
      h('div', { 'data-testid': 'auth-authenticated' }, String(auth.authenticated)),
      h('div', { 'data-testid': 'auth-user-name' }, auth.userName),
      h('div', { 'data-testid': 'auth-role-label' }, auth.primaryRoleLabel),
      h('div', { 'data-testid': 'auth-error' }, auth.error ?? ''),
    ])
  },
})

async function renderHarness() {
  return await render(AuthStoreHarness, {
    global: {
      plugins: [createPinia()],
    },
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  authMocks.state.snapshot = createSnapshot()
  authMocks.state.subscriber = null
})

test('initializes once, exposes the initializing state, and applies snapshot updates in the browser', async () => {
  await page.viewport(1280, 900)

  const deferred = createDeferred<AuthSnapshot>()
  authMocks.initializeAuth.mockImplementation(() => deferred.promise)

  const view = await renderHarness()

  await view.getByRole('button', { name: 'Initialize auth' }).click()
  await view.getByRole('button', { name: 'Initialize auth' }).click()

  expect(authMocks.initializeAuth).toHaveBeenCalledTimes(1)
  expect(view.getByTestId('auth-initializing').element().textContent).toBe('true')
  expect(view.getByTestId('auth-user-name').element().textContent).toBe('User')

  deferred.resolve(createSnapshot({
    initialized: true,
    authenticated: true,
    displayName: 'Alex Carter',
    roles: ['realm-admin'],
  }))

  await expect.poll(() => view.getByTestId('auth-initializing').element().textContent ?? '').toBe('false')
  expect(view.getByTestId('auth-initialized').element().textContent).toBe('true')
  expect(view.getByTestId('auth-authenticated').element().textContent).toBe('true')
  expect(view.getByTestId('auth-user-name').element().textContent).toBe('Alex Carter')
  expect(view.getByTestId('auth-role-label').element().textContent).toBe('Administrator')

  authMocks.state.subscriber?.(createSnapshot({
    initialized: true,
    authenticated: true,
    displayName: 'Pat Operator',
    roles: ['ngb-user'],
  }))

  await expect.poll(() => view.getByTestId('auth-user-name').element().textContent ?? '').toBe('Pat Operator')
  expect(view.getByTestId('auth-role-label').element().textContent).toBe('User')
})

test('keeps redirecting after successful login', async () => {
  await page.viewport(1280, 900)

  authMocks.loginWithKeycloak.mockResolvedValueOnce(undefined)

  const view = await renderHarness()
  await view.getByRole('button', { name: 'Login auth' }).click()

  expect(authMocks.loginWithKeycloak).toHaveBeenCalledWith('/reports/occupancy')
  await expect.poll(() => view.getByTestId('auth-redirecting').element().textContent ?? '').toBe('true')
  expect(view.getByTestId('auth-error').element().textContent).toBe('')
})

test('clears the redirect flag and exposes the browser-visible error when login fails', async () => {
  await page.viewport(1280, 900)

  authMocks.loginWithKeycloak.mockRejectedValueOnce(new Error('Login blocked'))

  const view = await renderHarness()
  await view.getByRole('button', { name: 'Login auth' }).click()

  await expect.poll(() => view.getByTestId('auth-redirecting').element().textContent ?? '').toBe('false')
  expect(view.getByTestId('auth-error').element().textContent).toBe('Login blocked')
})

test('keeps redirecting after successful logout', async () => {
  await page.viewport(1280, 900)

  authMocks.logoutFromKeycloak.mockResolvedValueOnce(undefined)

  const view = await renderHarness()
  await view.getByRole('button', { name: 'Logout auth' }).click()

  expect(authMocks.logoutFromKeycloak).toHaveBeenCalledTimes(1)
  await expect.poll(() => view.getByTestId('auth-redirecting').element().textContent ?? '').toBe('true')
  expect(view.getByTestId('auth-error').element().textContent).toBe('')
})

test('surfaces the generic browser-visible error message when logout fails', async () => {
  await page.viewport(1280, 900)

  authMocks.logoutFromKeycloak.mockRejectedValueOnce({ detail: 'denied' })

  const view = await renderHarness()
  await view.getByRole('button', { name: 'Logout auth' }).click()

  await expect.poll(() => view.getByTestId('auth-redirecting').element().textContent ?? '').toBe('false')
  expect(view.getByTestId('auth-error').element().textContent).toBe(
    'Unable to initialize Keycloak. Check the UI env vars and the client redirect URI settings.',
  )
})
