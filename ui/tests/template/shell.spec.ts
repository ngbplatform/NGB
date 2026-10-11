import { defineComponent, h, nextTick } from 'vue'
import { render } from 'vitest-browser-vue'
import { expect, it, vi } from 'vitest'
import preset from '../../ngb-ui-framework/tailwind-preset.js'

const state = vi.hoisted(() => ({
  push: vi.fn(), logout: vi.fn(), accessLoad: vi.fn(), menuLoad: vi.fn(),
  groups: [{ label: 'Administration', items: [{ route: '/roles', label: 'Roles', icon: 'shield' }] }],
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: state.push }), useRoute: () => ({ path: '/roles' }) }))
vi.mock('@ngbplatform/ui', () => ({
  useAuthStore: () => ({ userName: 'Administrator', logout: state.logout }),
  useAccessStore: () => ({ load: state.accessLoad }),
  useMainMenuStore: () => ({ groups: state.groups, load: state.menuLoad }),
  NgbSiteShell: defineComponent({
    props: ['nodes'],
    emits: ['navigate', 'select', 'sign-out'],
    setup(props, { emit, slots }) {
      return () => h('div', [
        h('pre', JSON.stringify(props.nodes)),
        h('button', { onClick: () => emit('navigate', '/users') }, 'Navigate'),
        h('button', { onClick: () => emit('select', 'roles', '/roles') }, 'Select'),
        h('button', { onClick: () => emit('sign-out') }, 'Sign out'),
        slots.default?.(),
      ])
    },
  }),
}))

it('maps platform navigation and forwards shell actions', async () => {
  const { default: App } = await import('../../../packaging/templates/content/web/src/App.vue')
  const screen = render(App, { global: { stubs: { RouterView: true } } })
  await nextTick()
  expect(state.accessLoad).toHaveBeenCalledOnce()
  expect(state.menuLoad).toHaveBeenCalledOnce()
  await screen.getByRole('button', { name: 'Navigate' }).click()
  await screen.getByRole('button', { name: 'Select' }).click()
  await screen.getByRole('button', { name: 'Sign out' }).click()
  expect(state.push.mock.calls).toEqual([['/users'], ['/roles']])
  expect(state.logout).toHaveBeenCalledOnce()
  expect(screen.container.textContent).toContain('Administration')
  expect(preset.darkMode).toBe('class')
  expect(preset.theme.extend.colors['ngb-bg']).toBe('var(--ngb-bg)')
})
