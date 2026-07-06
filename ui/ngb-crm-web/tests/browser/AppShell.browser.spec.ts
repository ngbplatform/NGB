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

vi.mock('ngb-ui-framework', async () => {
  const { h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    NgbCommandPaletteDialog: { name: 'NgbCommandPaletteDialog', render: () => null },
    NgbSiteShell: {
      name: 'NgbSiteShell',
      props: ['nodes', 'selectedId', 'moduleTitle'],
      setup(props: { nodes?: SiteNodeStub[]; selectedId?: string | null; moduleTitle?: string }) {
        return () => h('div', { 'data-testid': 'crm-shell' }, [
          h('div', { 'data-testid': 'crm-shell-title' }, props.moduleTitle ?? ''),
          h('div', { 'data-testid': 'crm-shell-selected-id' }, props.selectedId ?? ''),
          h('nav', { 'data-testid': 'crm-shell-nav' }, (props.nodes ?? []).flatMap((node) => [
            h('div', { 'data-testid': 'crm-shell-node' }, node.label),
            ...(node.children ?? []).map((child) => h('a', { href: child.route ?? '#', 'data-testid': 'crm-shell-node' }, child.label)),
          ])),
        ])
      },
    },
    normalizeNgbRouteAliasPath: (value: string | null | undefined) => String(value ?? '').trim(),
    useAuthStore: () => mocks.authStore,
    useAccessStore: () => mocks.accessStore,
    useCommandPaletteHotkeys: () => undefined,
    useCommandPaletteStore: () => mocks.paletteStore,
    useMainMenuStore: () => mocks.menuStore,
  }
})

import App from '../../src/App.vue'

type AuthStore = {
  initializing: boolean
  redirecting: boolean
  authenticated: boolean
  userName: string
  email: string
  error: string | null
  login: ReturnType<typeof vi.fn>
  initialize: ReturnType<typeof vi.fn>
  logout: ReturnType<typeof vi.fn>
}

type AccessStore = {
  current: { isBootstrapAdmin: boolean } | null
  applicationRoleNames: string[]
  load: ReturnType<typeof vi.fn>
  reset: ReturnType<typeof vi.fn>
}

type MenuStore = {
  groups: Array<{ label: string; ordinal: number; icon?: string | null; items: Array<{ kind: string; code: string; label: string; route: string; ordinal: number; icon?: string | null }> }>
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

function createAuthStore(overrides: Partial<AuthStore> = {}): AuthStore {
  return reactive({
    initializing: false,
    redirecting: false,
    authenticated: true,
    userName: 'CRM Tester',
    email: 'crm.tester@demo.ngbplatform.com',
    error: null,
    login: vi.fn(async () => undefined),
    initialize: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
    ...overrides,
  }) as AuthStore
}

beforeEach(() => {
  mocks.routerPush.mockReset()
  mocks.route = reactive({ fullPath: '/documents/crm.quote', path: '/documents/crm.quote', matched: [] }) as RouteState
  mocks.accessStore = reactive({
    current: { isBootstrapAdmin: false },
    applicationRoleNames: ['CRM Administrator'],
    load: vi.fn(async () => undefined),
    reset: vi.fn(),
  }) as AccessStore
  mocks.menuStore = {
    groups: [
      {
        label: 'Quotes',
        ordinal: 10,
        icon: 'file-text',
        items: [
          { kind: 'document', code: 'crm.quote', label: 'Quotes', route: '/documents/crm.quote', ordinal: 10, icon: 'file-text' },
        ],
      },
    ],
    load: vi.fn(async () => undefined),
  }
  mocks.paletteStore = {
    hydrate: vi.fn(async () => undefined),
    open: vi.fn(),
    setCurrentRoute: vi.fn(),
  }
  mocks.authStore = createAuthStore()
})

test('hydrates CRM shell navigation for an authenticated user', async () => {
  const view = await render(App, {
    global: {
      stubs: {
        RouterView: { name: 'RouterView', render: () => null },
      },
    },
  })

  await expect.element(view.getByTestId('crm-shell-title')).toHaveTextContent('CRM')
  await expect.element(view.getByText('Quotes', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('crm-shell-selected-id')).toHaveTextContent('group:quotes')
})

test('renders the blocking auth state while Keycloak initializes', async () => {
  mocks.authStore = createAuthStore({ authenticated: false, initializing: true })

  const view = await render(App, {
    global: {
      stubs: {
        RouterView: { name: 'RouterView', render: () => null },
      },
    },
  })

  await expect.element(view.getByText('Connecting to Keycloak', { exact: true })).toBeVisible()
})
