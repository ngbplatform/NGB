import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

const mocks = vi.hoisted(() => ({
  auth: { initialized: false, authenticated: false },
  summary: { value: null as null | { attentionCount: number } },
  refreshSummary: vi.fn(),
  connectRealtime: vi.fn(),
  toggleTheme: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAuthSnapshot: () => mocks.auth,
}))

vi.mock('../../../../src/ngb/site/useTheme', async () => {
  const { ref } = await import('vue')
  return {
    useTheme: () => ({
      resolved: ref<'light' | 'dark'>('light'),
      toggle: mocks.toggleTheme,
    }),
  }
})

vi.mock('../../../../src/ngb/primitives/toast', () => ({ provideToasts: vi.fn() }))
vi.mock('../../../../src/ngb/work-center/useWorkCenter', () => ({
  useWorkCenter: () => ({
    summary: mocks.summary,
    refreshSummary: mocks.refreshSummary,
    connectRealtime: mocks.connectRealtime,
  }),
}))

vi.mock('../../../../src/ngb/site/NgbTopBar.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    name: 'NgbTopBar',
    props: ['pageTitle', 'canBack', 'unreadNotifications', 'userName', 'userEmail', 'userMeta', 'userMetaIcon', 'userRoles'],
    emits: ['openMainMenu', 'openPalette', 'back', 'openNotifications', 'openHelp', 'openSettings', 'signOut', 'toggleTheme'],
    setup(props, { emit }) {
      const events = ['openMainMenu', 'openPalette', 'back', 'openNotifications', 'openHelp', 'openSettings', 'signOut', 'toggleTheme']
      return () => h('div', { 'data-testid': 'topbar-stub' }, [
        h('span', [props.pageTitle, props.canBack, props.unreadNotifications, props.userName, props.userEmail, props.userMeta, props.userMetaIcon, props.userRoles].join('|')),
        ...events.map((eventName) => h('button', {
          'data-event': eventName,
          onClick: () => emit(eventName as never),
        }, eventName)),
      ])
    },
  }) }
})

vi.mock('../../../../src/ngb/site/NgbSiteSidebar.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    name: 'NgbSiteSidebar',
    emits: ['toggleCollapsed', 'navigate', 'select'],
    setup(_, { emit }) {
      return () => h('div', { 'data-testid': 'sidebar-stub' }, [
        h('button', { 'data-event': 'toggle', onClick: () => emit('toggleCollapsed') }, 'toggle'),
        h('button', { 'data-event': 'navigate', onClick: () => emit('navigate', '/from-sidebar') }, 'navigate'),
        h('button', { 'data-event': 'select', onClick: () => emit('select', 'leaf', '/leaf') }, 'select'),
      ])
    },
  }) }
})

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    name: 'NgbDrawer',
    props: ['open', 'title'],
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => h('section', { 'data-title': props.title, 'data-open': String(props.open) }, [
        h('button', { 'data-drawer-update': 'false', onClick: () => emit('update:open', false) }, 'close drawer'),
        h('button', { 'data-drawer-update': 'true', onClick: () => emit('update:open', true) }, 'open drawer'),
        slots.default?.(),
      ])
    },
  }) }
})

vi.mock('../../../../src/ngb/work-center/NgbWorkCenterDrawer.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    name: 'NgbWorkCenterDrawer',
    emits: ['close'],
    setup(_, { emit }) {
      return () => h('button', { 'data-testid': 'work-center-close', onClick: () => emit('close') }, 'close')
    },
  }) }
})
vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: () => () => h('i') }) }
})
vi.mock('../../../../src/ngb/primitives/NgbToastHost.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: () => () => h('div') }) }
})

import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

const baseProps = {
  moduleTitle: 'CRM',
  productTitle: 'NGB',
  workCenterVertical: 'crm',
  unreadNotifications: 4,
  userName: ' Alice ',
  userEmail: ' alice@example.com ',
  userMeta: ' Admin ',
  userMetaIcon: 'shield-check' as const,
  userRoles: [' Sales ', ' ', 'Manager'],
  pinned: [],
  recent: [],
  nodes: [
    { id: 'first', label: 'First' },
    { id: 'group', label: 'Group', children: [{ id: 'selected', label: 'Selected page' }] },
  ],
  settings: [{
    label: 'Custom',
    items: [
      { label: 'With icon', route: '/with-icon', icon: 'settings', description: 'Description' },
      { label: 'Without icon', route: '/without-icon' },
    ],
  }],
  selectedId: 'selected',
}

function shell(props: Record<string, unknown> = {}) {
  return mount(NgbSiteShell, {
    props: { ...baseProps, ...props },
  })
}

describe('NgbSiteShell complete orchestration', () => {
  beforeEach(() => {
    mocks.auth.initialized = false
    mocks.auth.authenticated = false
    mocks.summary.value = null
    mocks.refreshSummary.mockReset().mockResolvedValue(undefined)
    mocks.connectRealtime.mockReset().mockResolvedValue(undefined)
    mocks.toggleTheme.mockReset()
  })

  it('forwards every shell event, drawer update, navigation, and computed presentation branch', async () => {
    const wrapper = shell()
    expect(wrapper.get('[data-testid="topbar-stub"]').text()).toContain('Selected page|false|4|Alice|alice@example.com|Admin|shield-check')
    expect(wrapper.text()).toContain('With icon')
    expect(wrapper.text()).toContain('Without icon')
    expect(wrapper.text()).toContain('Description')

    for (const button of wrapper.findAll('[data-testid="topbar-stub"] button')) await button.trigger('click')
    expect(wrapper.emitted('openPalette')).toHaveLength(1)
    expect(wrapper.emitted('back')).toHaveLength(1)
    expect(wrapper.emitted('signOut')).toHaveLength(1)
    expect(mocks.toggleTheme).toHaveBeenCalledOnce()

    for (const button of wrapper.findAll('[data-testid="sidebar-stub"] button')) await button.trigger('click')
    expect(wrapper.emitted('navigate')?.flat()).toContain('/from-sidebar')
    expect(wrapper.emitted('select')?.flat()).toContain('leaf')

    for (const button of wrapper.findAll('[data-drawer-update]')) await button.trigger('click')
    await wrapper.get('[data-testid="work-center-close"]').trigger('click')

    const settingButtons = wrapper.findAll('button').filter((button) => (
      button.text().includes('With icon') || button.text().includes('Without icon')
    ))
    for (const button of settingButtons) await button.trigger('click')
    expect(wrapper.emitted('navigate')?.flat()).toEqual(expect.arrayContaining(['/with-icon', '/without-icon']))

    mocks.summary.value = { attentionCount: 9 }
    const summaryWrapper = shell({ canBack: true })
    expect(summaryWrapper.get('[data-testid="topbar-stub"]').text()).toContain('Selected page|true|9')

    await wrapper.setProps({ pageTitle: ' Explicit ', selectedId: null })
    expect(wrapper.get('[data-testid="topbar-stub"]').text()).toContain('Explicit')
    await wrapper.setProps({ pageTitle: ' ', selectedId: 'missing' })
    expect(wrapper.get('[data-testid="topbar-stub"]').text()).toContain('CRM')
    mocks.summary.value = null
    await wrapper.setProps({
      pageTitle: ' ', selectedId: null, unreadNotifications: undefined,
      userName: undefined, userEmail: undefined, userMeta: undefined,
      userMetaIcon: 'user', userRoles: undefined, nodes: [], settings: undefined,
    })
    expect(wrapper.get('[data-testid="topbar-stub"]').text()).toContain('|false|0|User|||user|')
    await wrapper.setProps({ userMetaIcon: 'shield' })
    expect(wrapper.get('[data-testid="topbar-stub"]').text()).toContain('|shield|')
  })

  it('starts realtime only for an initialized authenticated session and covers both vertical arguments', async () => {
    mocks.auth.initialized = false
    mocks.auth.authenticated = true
    shell()
    expect(mocks.refreshSummary).not.toHaveBeenCalled()

    mocks.auth.initialized = true
    mocks.auth.authenticated = true
    mocks.refreshSummary.mockRejectedValueOnce(new Error('temporary'))
    shell()
    await vi.waitFor(() => {
      expect(mocks.refreshSummary).toHaveBeenCalledWith('crm')
      expect(mocks.connectRealtime).toHaveBeenCalledOnce()
    })

    mocks.refreshSummary.mockClear()
    shell({ workCenterVertical: '' })
    await vi.waitFor(() => expect(mocks.refreshSummary).toHaveBeenCalledWith(null))
  })
})
