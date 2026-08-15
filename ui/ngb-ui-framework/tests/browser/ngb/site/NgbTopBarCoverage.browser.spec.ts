import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'

import NgbTopBar from '../../../../src/ngb/site/NgbTopBar.vue'

const MenuButtonStub = defineComponent({
  name: 'MenuButtonCoverageStub',
  inheritAttrs: false,
  setup(_, { attrs, slots }) {
    return () => h('button', attrs, slots.default?.())
  },
})

const MenuItemsStub = defineComponent({
  name: 'MenuItemsCoverageStub',
  setup(_, { slots }) {
    return () => h('div', slots.default?.())
  },
})

const IconStub = defineComponent({
  name: 'NgbIconCoverageStub',
  props: ['name'],
  setup(props) {
    return () => h('i', String(props.name ?? ''))
  },
})

function createMenuStub(open: boolean) {
  return defineComponent({
    name: 'MenuCoverageStub',
    setup(_, { slots }) {
      return () => h('div', slots.default?.({ open }))
    },
  })
}

function createMenuItemStub(active: boolean) {
  return defineComponent({
    name: 'MenuItemCoverageStub',
    setup(_, { slots }) {
      return () => h('div', slots.default?.({ active }))
    },
  })
}

function mountTopBar(props: Record<string, unknown>, open = true, active = true) {
  return mount(NgbTopBar, {
    props: {
      pageTitle: 'Payments',
      canBack: false,
      unreadNotifications: 128,
      themeResolved: 'dark',
      userName: 'Alex Carter',
      userEmail: 'alex@example.com',
      userMeta: 'Administrator',
      userMetaIcon: 'shield-check',
      userRoles: [' Admin ', 'Reviewer', 'Auditor', 'Support'],
      hasSettings: true,
      showMainMenu: true,
      ...props,
    },
    global: {
      stubs: {
        Menu: createMenuStub(open),
        MenuButton: MenuButtonStub,
        MenuItems: MenuItemsStub,
        MenuItem: createMenuItemStub(active),
        NgbIcon: IconStub,
        transition: false,
      },
    },
  })
}

async function clickEveryAction(wrapper: ReturnType<typeof mountTopBar>) {
  for (const button of wrapper.findAll('button')) {
    await button.trigger('click')
  }
}

describe('NgbTopBar complete responsive behavior', () => {
  it('renders every responsive action and executes both active menu branches', async () => {
    const wrapper = mountTopBar({})

    expect(wrapper.text()).toContain('99+')
    expect(wrapper.text()).toContain('+1 more')
    expect(wrapper.text()).toContain('Application roles')
    expect(wrapper.text()).not.toContain('Administrator')
    expect(wrapper.text()).toContain('AC')
    await clickEveryAction(wrapper)

    expect(wrapper.emitted('openMainMenu')).toHaveLength(1)
    expect(wrapper.emitted('openPalette')).toHaveLength(1)
    expect(wrapper.emitted('openNotifications')).toHaveLength(3)
    expect(wrapper.emitted('openHelp')).toHaveLength(3)
    expect(wrapper.emitted('openSettings')).toHaveLength(3)
    expect(wrapper.emitted('toggleTheme')).toHaveLength(3)
    expect(wrapper.emitted('signOut')).toHaveLength(3)

    const inactive = mountTopBar({}, false, false)
    await clickEveryAction(inactive)
    expect(inactive.findAll('.text-ngb-danger')).not.toHaveLength(0)
  })

  it('covers empty, metadata, duplicate-role, light-theme, and badge variants', async () => {
    const originalPlatform = navigator.platform
    Object.defineProperty(navigator, 'platform', { configurable: true, value: 'Linux' })
    const wrapper = mountTopBar({
      unreadNotifications: 1,
      themeResolved: 'light',
      userName: 'Q',
      userEmail: undefined,
      userRoles: [],
      userMeta: 'Operator',
      userMetaIcon: 'shield',
      hasSettings: false,
      showMainMenu: false,
    })

    expect(wrapper.text()).toContain('Operator')
    expect(wrapper.text()).not.toContain('Application roles')
    expect(wrapper.text()).toContain('Q')
    expect(wrapper.findAll('button[title="Settings"]')).toHaveLength(0)
    expect(wrapper.findAll('[data-testid="site-topbar-main-menu"]')).toHaveLength(0)

    await wrapper.setProps({ hasSettings: true, showMainMenu: true, themeResolved: 'dark' })
    expect(wrapper.findAll('button[title="Settings"]')).not.toHaveLength(0)
    expect(wrapper.findAll('[data-testid="site-topbar-main-menu"]')).toHaveLength(1)
    expect(wrapper.findAll('button[title="Switch to light mode"]')).not.toHaveLength(0)
    await wrapper.setProps({ hasSettings: false, showMainMenu: false, themeResolved: 'light' })
    expect(wrapper.findAll('button[title="Switch to dark mode"]')).not.toHaveLength(0)

    await wrapper.setProps({ userMetaIcon: 'user' })
    expect(wrapper.text()).toContain('Operator')
    await wrapper.setProps({
      unreadNotifications: 0,
      userName: undefined,
      userEmail: undefined,
      userMeta: undefined,
      userMetaIcon: 'user',
      userRoles: undefined,
    })
    expect(wrapper.text()).toContain('User')
    expect(wrapper.text()).not.toContain('Operator')
    await wrapper.setProps({ userRoles: [' ', 'Admin', ' admin ', 'Editor'] })
    expect(wrapper.text()).toContain('Admin')
    expect(wrapper.text()).toContain('Editor')
    expect(wrapper.text()).not.toContain('99+')
    expect(wrapper.text()).not.toContain('Operator')
    Object.defineProperty(navigator, 'platform', { configurable: true, value: originalPlatform })
  })
})
