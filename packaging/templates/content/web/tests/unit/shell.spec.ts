import { defineComponent, h, nextTick } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, expect, it, vi } from 'vitest'
import preset from '@ngbplatform/ui/tailwind-preset'

vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081',
  VITE_KEYCLOAK_REALM: 'component-tests',
  VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))

const state = vi.hoisted(() => ({
  push: vi.fn(), logout: vi.fn(), accessLoad: vi.fn(), menuLoad: vi.fn(),
  openPalette: vi.fn(), paletteHotkeys: vi.fn(),
  meta: {} as { bare?: boolean },
  groups: [
    { label: 'Dashboard', icon: 'home', items: [{ route: '/', label: 'Dashboard', icon: 'home' }] },
    { label: 'Setup & Controls', items: [
      { route: '/roles', label: 'Roles', icon: 'shield' },
      { route: 'http://localhost:5184/hangfire', label: 'Background Jobs', icon: 'activity' },
    ] },
    { label: 'Empty', items: [] },
  ],
}))
vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ push: state.push }),
  useRoute: () => ({ path: '/roles', meta: state.meta }),
}))
vi.mock('@ngbplatform/ui', async importOriginal => {
  const { NgbSiteSidebar } = await importOriginal<typeof import('@ngbplatform/ui')>()
  return {
    useAuthStore: () => ({ userName: 'Administrator', logout: state.logout }),
    useAccessStore: () => ({ load: state.accessLoad }),
    useMainMenuStore: () => ({ groups: state.groups, load: state.menuLoad }),
    useCommandPaletteHotkeys: state.paletteHotkeys,
    useCommandPaletteStore: () => ({ open: state.openPalette }),
    NgbCommandPaletteDialog: defineComponent(() => () => h('div', 'Command palette')),
    NgbSiteShell: defineComponent({
      props: ['nodes'],
      emits: ['navigate', 'select', 'sign-out', 'open-palette'],
      setup(props, { emit, slots }) {
        return () => h('div', [
          h('pre', JSON.stringify(props.nodes)),
          h(NgbSiteSidebar, {
            nodes: props.nodes,
            selectedId: '/roles',
            collapsed: false,
            onNavigate: (target: string) => emit('navigate', target),
            onSelect: (id: string, target: string) => emit('select', id, target),
          }),
          h('button', { onClick: () => emit('sign-out') }, 'Sign out'),
          h('button', { onClick: () => emit('open-palette') }, 'Search'),
          slots.default?.(),
        ])
      },
    }),
  }
})

afterEach(() => {
  delete state.meta.bare
  vi.clearAllMocks()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

it('renders only the routed page for a standalone preview', async () => {
  state.meta.bare = true
  const { default: App } = await import('../../src/App.vue')
  const preview = defineComponent(() => () => h('article', 'Printable document'))
  const screen = render(App, { global: { stubs: { RouterView: preview } } })
  expect(screen.container.textContent).toBe('Printable document')
})

it('renders standalone navigation and navigates once per real sidebar click', async () => {
  const opened = vi.spyOn(window, 'open').mockReturnValue(null)
  const { default: App } = await import('../../src/App.vue')
  const screen = render(App, { global: { stubs: { RouterView: true } } })
  await nextTick()
  expect(state.accessLoad).toHaveBeenCalledOnce()
  expect(state.menuLoad).toHaveBeenCalledOnce()
  const dashboard = screen.getByRole('button', { name: 'Dashboard', exact: true })
  expect(dashboard.element().querySelector('svg[data-icon="home"]')).toBeVisible()
  await dashboard.click()
  await screen.getByRole('button', { name: 'Roles', exact: true }).click()
  await screen.getByRole('button', { name: 'Background Jobs', exact: true }).click()
  await screen.getByRole('button', { name: 'Sign out' }).click()
  await screen.getByRole('button', { name: 'Search' }).click()
  expect(state.openPalette).toHaveBeenCalledOnce()
  expect(state.paletteHotkeys).toHaveBeenCalledOnce()
  expect(screen.container.textContent).toContain('Command palette')
  expect(state.push.mock.calls).toEqual([['/'], ['/roles']])
  expect(opened).toHaveBeenCalledExactlyOnceWith('http://localhost:5184/hangfire', '_blank', 'noopener')
  expect(state.logout).toHaveBeenCalledOnce()
  expect(screen.container.textContent).toContain('Setup & Controls')
  expect(screen.container.textContent).toContain('"id":"/","label":"Dashboard","route":"/"')
  expect(preset.darkMode).toBe('class')
  expect(preset.theme.extend.colors['ngb-bg']).toBe('var(--ngb-bg)')
})
