import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, reactive } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'

import { createAuthGuard, type AuthGuardStore } from '../../../../src/ngb/auth/router'

const GuardHarness = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'current-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

const PublicPage = {
  render: () => h('div', { 'data-testid': 'page-public' }, 'Public page'),
}

const ProtectedPage = {
  render: () => h('div', { 'data-testid': 'page-protected' }, 'Protected page'),
}

function createStore(overrides: Partial<AuthGuardStore> = {}): AuthGuardStore {
  return reactive({
    redirecting: false,
    initialized: false,
    initializing: false,
    authenticated: false,
    error: null,
    initialize: vi.fn(async () => undefined),
    login: vi.fn(async () => undefined),
    ...overrides,
  }) as AuthGuardStore
}

async function renderGuardHarness(auth: AuthGuardStore, initialRoute = '/public') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/public',
        meta: { public: true },
        component: PublicPage,
      },
      {
        path: '/protected',
        component: ProtectedPage,
      },
    ],
  })

  router.beforeEach(createAuthGuard(() => auth))
  await router.push(initialRoute)
  await router.isReady()

  const view = await render(GuardHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    router,
    view,
  }
}

test('bypasses auth initialization for public routes', async () => {
  await page.viewport(1280, 900)

  const auth = createStore()
  const { view } = await renderGuardHarness(auth, '/public')

  await expect.element(view.getByTestId('page-public')).toBeVisible()
  expect(auth.initialize).not.toHaveBeenCalled()
  expect(auth.login).not.toHaveBeenCalled()
})

test('initializes auth and allows protected routes once the session becomes authenticated', async () => {
  await page.viewport(1280, 900)

  const auth = createStore()
  auth.initialize = vi.fn(async () => {
    auth.initialized = true
    auth.authenticated = true
  })

  const { view } = await renderGuardHarness(auth, '/protected')

  await expect.element(view.getByTestId('page-protected')).toBeVisible()
  expect(auth.initialize).toHaveBeenCalledTimes(1)
  expect(auth.login).not.toHaveBeenCalled()
})

test('lets the protected route mount when auth initialization fails so the app can render its error UI', async () => {
  await page.viewport(1280, 900)

  const auth = createStore({
    initialize: vi.fn(async () => {
      throw new Error('Keycloak init failed')
    }),
  })

  const { view } = await renderGuardHarness(auth, '/protected')

  await expect.element(view.getByTestId('page-protected')).toBeVisible()
  expect(auth.login).not.toHaveBeenCalled()
})

test('starts login for blocked protected navigation and keeps the current route stable', async () => {
  await page.viewport(1280, 900)

  const auth = createStore({
    initialized: true,
  })
  const { router, view } = await renderGuardHarness(auth, '/public')

  await router.push('/protected?tab=details')

  expect(auth.login).toHaveBeenCalledWith('/protected?tab=details')
  await expect.element(view.getByTestId('page-public')).toBeVisible()
  expect(view.getByTestId('current-route').element().textContent).toBe('/public')
})

test('blocks protected navigation while an auth redirect is already in progress', async () => {
  await page.viewport(1280, 900)

  const auth = createStore({
    redirecting: true,
    initialized: true,
  })
  const { router, view } = await renderGuardHarness(auth, '/public')

  await router.push('/protected?tab=details')

  expect(auth.initialize).not.toHaveBeenCalled()
  expect(auth.login).not.toHaveBeenCalled()
  await expect.element(view.getByTestId('page-public')).toBeVisible()
  expect(view.getByTestId('current-route').element().textContent).toBe('/public')
})

test('blocks protected navigation when auth is already initialized with an error state', async () => {
  await page.viewport(1280, 900)

  const auth = createStore({
    initialized: true,
    error: 'Keycloak unavailable',
  })
  const { router, view } = await renderGuardHarness(auth, '/public')

  await router.push('/protected?tab=details')

  expect(auth.initialize).not.toHaveBeenCalled()
  expect(auth.login).not.toHaveBeenCalled()
  await expect.element(view.getByTestId('page-public')).toBeVisible()
  expect(view.getByTestId('current-route').element().textContent).toBe('/public')
})
