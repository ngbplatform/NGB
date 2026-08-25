import { h, nextTick, reactive } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

type AuthStore = {
  initializing: boolean
  redirecting: boolean
  authenticated: boolean
  userName: string
  email: string | null
  primaryRoleLabel: string
  primaryRoleIcon: string | null
  error: string | null
  login: ReturnType<typeof vi.fn>
  initialize: ReturnType<typeof vi.fn>
  logout: ReturnType<typeof vi.fn>
}

type MenuItem = {
  kind: string
  code: string
  label: string
  route: string | null
  ordinal: number
  icon?: string | null
}

type MenuGroup = {
  label: string
  ordinal: number
  icon?: string | null
  items: MenuItem[]
}

type MenuStore = {
  groups: MenuGroup[] | null
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

type SiteNodeStub = {
  label: string
  route?: string | null
  children?: SiteNodeStub[]
}

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

vi.mock('@ngbplatform/ui', async () => {
  const { h: renderNode } = await vi.importActual<typeof import('vue')>('vue')

  return {
    NgbCommandPaletteDialog: {
      name: 'NgbCommandPaletteDialog',
      render: () => renderNode('div', { 'data-testid': 'crm-palette-dialog' }),
    },
    NgbSiteShell: {
      name: 'NgbSiteShell',
      props: ['moduleTitle', 'nodes', 'selectedId', 'userEmail', 'userMeta', 'userMetaIcon'],
      emits: ['navigate', 'select', 'openPalette', 'signOut'],
      setup(
        props: {
          moduleTitle?: string
          nodes?: SiteNodeStub[]
          selectedId?: string | null
          userEmail?: string
          userMeta?: string
          userMetaIcon?: string
        },
        { emit, slots }: { emit: (event: string, ...args: unknown[]) => void; slots: Record<string, () => unknown> },
      ) {
        return () => renderNode('div', { 'data-testid': 'crm-shell' }, [
          renderNode('div', { 'data-testid': 'crm-shell-title' }, props.moduleTitle ?? ''),
          renderNode('div', { 'data-testid': 'crm-shell-selected-id' }, props.selectedId ?? ''),
          renderNode('div', { 'data-testid': 'crm-shell-email' }, props.userEmail ?? ''),
          renderNode('div', { 'data-testid': 'crm-shell-meta' }, props.userMeta ?? ''),
          renderNode('div', { 'data-testid': 'crm-shell-meta-icon' }, props.userMetaIcon ?? ''),
          renderNode('nav', { 'data-testid': 'crm-shell-nav' }, (props.nodes ?? []).flatMap((node) => [
            renderNode('div', { 'data-testid': 'crm-shell-node' }, node.label),
            ...(node.children ?? []).map((child) => renderNode('div', { 'data-testid': 'crm-shell-node' }, child.label)),
          ])),
          renderNode('button', { type: 'button', onClick: () => emit('navigate', '') }, 'Navigate empty'),
          renderNode('button', { type: 'button', onClick: () => emit('navigate', null) }, 'Navigate null'),
          renderNode('button', { type: 'button', onClick: () => emit('navigate', '/clients') }, 'Navigate internal'),
          renderNode('button', { type: 'button', onClick: () => emit('navigate', 'https://status.example/crm') }, 'Navigate external'),
          renderNode('button', { type: 'button', onClick: () => emit('select', 'client', '/projects') }, 'Select route'),
          renderNode('button', { type: 'button', onClick: () => emit('openPalette') }, 'Open palette'),
          renderNode('button', { type: 'button', onClick: () => emit('signOut') }, 'Sign out'),
          slots.default?.(),
        ])
      },
    },
    normalizeNgbRouteAliasPath: (value: string | null | undefined) => String(value ?? '').trim(),
    useAuthStore: () => mocks.authStore,
    useCommandPaletteHotkeys: () => undefined,
    useCommandPaletteStore: () => mocks.paletteStore,
    useMainMenuStore: () => mocks.menuStore,
  }
})

import App from '../../src/App.vue'

function createAuthStore(overrides: Partial<AuthStore> = {}): AuthStore {
  return reactive({
    initializing: false,
    redirecting: false,
    authenticated: false,
    userName: 'CRM Tester',
    email: 'crm@example.com',
    primaryRoleLabel: 'CRM Administrator',
    primaryRoleIcon: 'shield-check',
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
          render: () => h('div', { 'data-testid': 'crm-router-view' }),
        },
      },
    },
  })
}

beforeEach(() => {
  vi.restoreAllMocks()
  mocks.routerPush.mockReset()
  mocks.route = reactive({ fullPath: '/clients/active', path: '/clients/active', matched: [{}] }) as RouteState
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

test.each([
  ['initializing', 'Connecting to Keycloak', 'Checking whether an existing SSO session is already available.'],
  ['redirecting', 'Redirecting to secure sign-in', 'You will be sent to the login page in a moment.'],
] as const)('renders the %s blocking authentication state', async (state, title, detail) => {
  mocks.authStore = createAuthStore({ [state]: true })

  const view = await renderApp()

  await expect.element(view.getByText(title, { exact: true })).toBeVisible()
  await expect.element(view.getByText(detail, { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('Retry')
})

test('retries and starts direct sign-in from the blocking error state', async () => {
  const login = vi.fn(async () => undefined)
  const initialize = vi.fn(async () => {
    mocks.authStore!.error = null
  })
  mocks.authStore = createAuthStore({ error: 'Identity unavailable', login, initialize })

  const view = await renderApp()
  await expect.element(view.getByText('Unable to start the secure session', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Identity unavailable', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Sign in' }).click()
  await expect.poll(() => login.mock.calls.length).toBe(1)
  await view.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => login.mock.calls.length).toBe(2)
  expect(login).toHaveBeenNthCalledWith(1, '/clients/active')
  expect(login).toHaveBeenNthCalledWith(2, '/clients/active')
})

test('does not login after retry when authentication succeeds, an error remains, or initialization rejects', async () => {
  const login = vi.fn(async () => undefined)

  const authenticatedInitialize = vi.fn(async () => {
    mocks.authStore!.authenticated = true
    mocks.authStore!.error = null
  })
  mocks.authStore = createAuthStore({ error: 'Retry', initialize: authenticatedInitialize, login })
  const authenticated = await renderApp()
  await authenticated.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => authenticatedInitialize.mock.calls.length).toBe(1)
  authenticated.unmount()

  const failedInitialize = vi.fn(async () => undefined)
  mocks.authStore = createAuthStore({ error: 'Still failed', initialize: failedInitialize, login })
  const failed = await renderApp()
  await failed.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => failedInitialize.mock.calls.length).toBe(1)
  failed.unmount()

  const rejectedInitialize = vi.fn(async () => { throw new Error('offline') })
  mocks.authStore = createAuthStore({ error: 'Offline', initialize: rejectedInitialize, login })
  const rejected = await renderApp()
  await rejected.getByRole('button', { name: 'Retry' }).click()
  await expect.poll(() => rejectedInitialize.mock.calls.length).toBe(1)
  expect(login).not.toHaveBeenCalled()
})

test('hydrates a complete shell and handles navigation, palette, and logout actions', async () => {
  const logout = vi.fn(async () => undefined)
  mocks.authStore = createAuthStore({
    authenticated: true,
    email: null,
    primaryRoleIcon: null,
    logout,
  })
  mocks.menuStore = {
    groups: [
      {
        label: '!!!',
        ordinal: 20,
        icon: null,
        items: [{ kind: 'catalog', code: 'clients', label: '!!!', route: '/clients', ordinal: 1, icon: 'users' }],
      },
      {
        label: 'Empty route',
        ordinal: 10,
        icon: null,
        items: [{ kind: 'catalog', code: 'empty', label: 'Empty route', route: null, ordinal: 1, icon: null }],
      },
      {
        label: 'No icons',
        ordinal: 30,
        icon: null,
        items: [
          { kind: 'catalog', code: 'later', label: 'Later', route: '/later', ordinal: 2, icon: null },
          { kind: 'catalog', code: 'first', label: 'First', route: '/first', ordinal: 1, icon: 'star' },
        ],
      },
    ],
    load: vi.fn(async () => undefined),
  }
  const open = vi.spyOn(window, 'open').mockImplementation(() => null)

  const view = await renderApp()
  await expect.poll(() => mocks.menuStore?.load.mock.calls.length ?? 0).toBe(1)
  expect(mocks.paletteStore?.hydrate).toHaveBeenCalledOnce()
  expect(mocks.paletteStore?.setCurrentRoute).toHaveBeenCalledWith('/clients/active')
  await expect.element(view.getByTestId('crm-shell-title')).toHaveTextContent('CRM')
  await expect.element(view.getByTestId('crm-shell-selected-id')).toHaveTextContent('group:menu')
  await expect.element(view.getByTestId('crm-shell-email')).toHaveTextContent('')
  await expect.element(view.getByTestId('crm-shell-meta-icon')).toHaveTextContent('')
  expect(Array.from(document.querySelectorAll('[data-testid="crm-shell-node"]'), (element) => element.textContent)).toEqual([
    'Empty route', '!!!', 'No icons', 'First', 'Later',
  ])

  await view.getByRole('button', { name: 'Navigate empty' }).click()
  await view.getByRole('button', { name: 'Navigate null' }).click()
  expect(mocks.routerPush).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Navigate internal' }).click()
  await view.getByRole('button', { name: 'Select route' }).click()
  await view.getByRole('button', { name: 'Navigate external' }).click()
  expect(mocks.routerPush).toHaveBeenNthCalledWith(1, '/clients')
  expect(mocks.routerPush).toHaveBeenNthCalledWith(2, '/projects')
  expect(open).toHaveBeenCalledWith('https://status.example/crm', '_self')

  await view.getByRole('button', { name: 'Open palette' }).click()
  await view.getByRole('button', { name: 'Sign out' }).click()
  expect(mocks.paletteStore?.open).toHaveBeenCalledOnce()
  expect(logout).toHaveBeenCalledOnce()
  expect(view.getByTestId('crm-palette-dialog').element()).not.toBeNull()
  expect(view.getByTestId('crm-router-view').element()).not.toBeNull()
})

test('selects an exact child route and handles an empty current path', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  mocks.route = reactive({ fullPath: '', path: '', matched: [] }) as RouteState
  mocks.menuStore = {
    groups: [{
      label: 'Group',
      ordinal: 1,
      items: [
        { kind: 'catalog', code: 'first', label: 'First', route: '/first', ordinal: 1 },
        { kind: 'catalog', code: 'second', label: 'Second', route: '/second', ordinal: 2 },
      ],
    }],
    load: vi.fn(async () => undefined),
  }

  const view = await renderApp()
  await expect.element(view.getByTestId('crm-shell-selected-id')).toHaveTextContent('')
  mocks.route.path = '/second'
  mocks.route.fullPath = '/second'
  await expect.element(view.getByTestId('crm-shell-selected-id')).toHaveTextContent('catalog:second')
  expect(mocks.paletteStore?.setCurrentRoute).toHaveBeenLastCalledWith('/second')
})

test('renders empty navigation for an absent group collection', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  mocks.menuStore = { groups: null, load: vi.fn(async () => undefined) }

  const view = await renderApp()

  await expect.element(view.getByTestId('crm-shell-nav')).toHaveTextContent('')
  expect(document.querySelectorAll('[data-testid="crm-shell-node"]')).toHaveLength(0)
})

test('renders a bare route without shell chrome and palette', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  mocks.route = reactive({ fullPath: '/print', path: '/print', matched: [{ meta: { bare: true } }] }) as RouteState

  const view = await renderApp()

  expect(view.getByTestId('crm-router-view').element()).not.toBeNull()
  expect(document.querySelector('[data-testid="crm-shell"]')).toBeNull()
  expect(document.querySelector('[data-testid="crm-palette-dialog"]')).toBeNull()
})

test('rehydrates menu and palette after logout and login', async () => {
  mocks.authStore = createAuthStore({ authenticated: true })
  const view = await renderApp()
  await expect.poll(() => mocks.menuStore?.load.mock.calls.length ?? 0).toBe(1)

  mocks.authStore.authenticated = false
  await nextTick()
  mocks.authStore.authenticated = true
  await expect.poll(() => mocks.menuStore?.load.mock.calls.length ?? 0).toBe(2)
  expect(mocks.paletteStore?.hydrate).toHaveBeenCalledTimes(2)
  expect(view.getByTestId('crm-shell').element()).not.toBeNull()
})
