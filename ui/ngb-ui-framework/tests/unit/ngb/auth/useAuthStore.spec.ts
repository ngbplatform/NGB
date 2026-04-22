import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

describe('useAuthStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    authMocks.state.snapshot = createSnapshot()
    authMocks.state.subscriber = null
  })

  it('derives user-facing role metadata from the initial snapshot and subscription updates', () => {
    authMocks.state.snapshot = createSnapshot({
      initialized: true,
      authenticated: true,
      preferredUsername: 'jdoe',
      roles: ['pm-manager', 'realm-admin', 'ngb-user'],
    })

    const store = useAuthStore()

    expect(authMocks.subscribeAuth).toHaveBeenCalledTimes(1)
    expect(store.initialized).toBe(true)
    expect(store.authenticated).toBe(true)
    expect(store.userName).toBe('jdoe')
    expect(store.isAdmin).toBe(true)
    expect(store.primaryTechnicalRole).toBe('realm-admin')
    expect(store.friendlyRoles).toEqual(['Administrator', 'User', 'Pm Manager'])
    expect(store.primaryRoleLabel).toBe('Administrator')
    expect(store.primaryRoleIcon).toBe('shield-check')
    expect(store.hasRole(' realm-admin ')).toBe(true)

    authMocks.state.subscriber?.(createSnapshot({
      initialized: true,
      authenticated: true,
      displayName: 'Alex Carter',
      email: 'alex@example.com',
      roles: ['pm-operations'],
    }))

    expect(store.userName).toBe('Alex Carter')
    expect(store.isAdmin).toBe(false)
    expect(store.friendlyRoles).toEqual(['Pm Operations'])
    expect(store.primaryRoleLabel).toBe('Pm Operations')
    expect(store.primaryRoleIcon).toBe('user')
  })

  it('deduplicates initialize calls and applies the resolved auth snapshot once', async () => {
    const deferred = createDeferred<AuthSnapshot>()
    authMocks.initializeAuth.mockImplementation(() => deferred.promise)

    const store = useAuthStore()

    const first = store.initialize()
    const second = store.initialize()

    expect(authMocks.initializeAuth).toHaveBeenCalledTimes(1)
    expect(store.initializing).toBe(true)
    expect(store.error).toBeNull()

    deferred.resolve(createSnapshot({
      initialized: true,
      authenticated: true,
      displayName: 'Alex Carter',
      roles: ['admin'],
    }))

    await first
    await second

    expect(store.initialized).toBe(true)
    expect(store.initializing).toBe(false)
    expect(store.userName).toBe('Alex Carter')
    expect(store.isAdmin).toBe(true)

    await store.initialize()
    expect(authMocks.initializeAuth).toHaveBeenCalledTimes(1)
  })

  it('stores a friendly initialize error message when Keycloak setup fails', async () => {
    authMocks.initializeAuth.mockRejectedValueOnce(new Error('Keycloak init failed'))

    const store = useAuthStore()

    await expect(store.initialize()).rejects.toThrow('Keycloak init failed')

    expect(store.initializing).toBe(false)
    expect(store.error).toBe('Keycloak init failed')
  })

  it('keeps redirecting on successful login and resets the flag when login fails', async () => {
    authMocks.loginWithKeycloak.mockResolvedValueOnce(undefined)

    const successStore = useAuthStore()
    await successStore.login('/reports/occupancy')

    expect(authMocks.loginWithKeycloak).toHaveBeenCalledWith('/reports/occupancy')
    expect(successStore.redirecting).toBe(true)
    expect(successStore.error).toBeNull()

    setActivePinia(createPinia())
    authMocks.loginWithKeycloak.mockRejectedValueOnce(new Error('Login blocked'))

    const failingStore = useAuthStore()
    await expect(failingStore.login('/reports/occupancy')).rejects.toThrow('Login blocked')

    expect(failingStore.redirecting).toBe(false)
    expect(failingStore.error).toBe('Login blocked')
  })

  it('keeps redirecting on successful logout and falls back to a generic message on logout failure', async () => {
    authMocks.logoutFromKeycloak.mockResolvedValueOnce(undefined)

    const successStore = useAuthStore()
    await successStore.logout()

    expect(authMocks.logoutFromKeycloak).toHaveBeenCalledTimes(1)
    expect(successStore.redirecting).toBe(true)
    expect(successStore.error).toBeNull()

    setActivePinia(createPinia())
    authMocks.logoutFromKeycloak.mockRejectedValueOnce({ detail: 'denied' })

    const failingStore = useAuthStore()
    await expect(failingStore.logout()).rejects.toEqual({ detail: 'denied' })

    expect(failingStore.redirecting).toBe(false)
    expect(failingStore.error).toBe('Unable to initialize Keycloak. Check the UI env vars and the client redirect URI settings.')
  })
})
