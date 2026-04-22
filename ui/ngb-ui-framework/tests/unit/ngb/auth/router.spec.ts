import { describe, expect, it, vi } from 'vitest'

import { createAuthGuard, type AuthGuardStore } from '../../../../src/ngb/auth/router'

function createStore(overrides: Partial<AuthGuardStore> = {}): AuthGuardStore {
  return {
    redirecting: false,
    initialized: false,
    initializing: false,
    authenticated: false,
    error: null,
    initialize: vi.fn(async () => undefined),
    login: vi.fn(async () => undefined),
    ...overrides,
  }
}

function createRoute(overrides: Record<string, unknown> = {}) {
  return {
    fullPath: '/reports/accounting.posting_log?periodFrom=2026-01',
    meta: {},
    ...overrides,
  }
}

describe('auth route guard', () => {
  it('allows public routes without touching the auth store', async () => {
    const auth = createStore()
    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute({ meta: { public: true } }) as never)).resolves.toBe(true)
    expect(auth.initialize).not.toHaveBeenCalled()
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('lets the app mount when auth initialization throws so the error UI can render', async () => {
    const auth = createStore({
      initialize: vi.fn(async () => {
        throw new Error('Keycloak init failed')
      }),
    })
    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute() as never)).resolves.toBe(true)
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('allows navigation after initialization authenticates the user', async () => {
    const auth = createStore()
    auth.initialize = vi.fn(async () => {
      auth.initialized = true
      auth.authenticated = true
    })

    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute() as never)).resolves.toBe(true)
    expect(auth.initialize).toHaveBeenCalledTimes(1)
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('blocks navigation while an auth redirect is already in progress', async () => {
    const auth = createStore({
      redirecting: true,
      initialized: true,
    })
    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute() as never)).resolves.toBe(false)
    expect(auth.initialize).not.toHaveBeenCalled()
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('starts login for unauthenticated protected routes after initialization completes cleanly', async () => {
    const auth = createStore({
      initialized: true,
    })
    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute() as never)).resolves.toBe(false)
    expect(auth.login).toHaveBeenCalledWith('/reports/accounting.posting_log?periodFrom=2026-01')
  })

  it('stays blocked on the current screen when the auth store already holds an error', async () => {
    const auth = createStore({
      initialized: true,
      error: 'Keycloak did not respond.',
    })
    const guard = createAuthGuard(() => auth)

    await expect(guard(createRoute() as never)).resolves.toBe(false)
    expect(auth.login).not.toHaveBeenCalled()
  })
})
