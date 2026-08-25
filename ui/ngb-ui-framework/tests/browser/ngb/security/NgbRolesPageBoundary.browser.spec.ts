import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia, setActivePinia } from 'pinia'

const state = vi.hoisted(() => ({
  getCurrentAccess: vi.fn(),
  getRoles: vi.fn(),
  routerPush: vi.fn(),
  layouts: [] as Array<Record<string, unknown>>,
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: state.getCurrentAccess,
  getRoles: state.getRoles,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: state.routerPush }),
}))

vi.mock('../../../../src/ngb/metadata/NgbRegisterPageLayout.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      props: {
        rows: { type: Array, default: () => [] },
        columns: { type: Array, default: () => [] },
        error: { type: String, default: '' },
        loading: Boolean,
        disableCreate: Boolean,
      },
      emits: ['back', 'refresh', 'create', 'rowActivate'],
      setup(props, { emit, slots }) {
        state.layouts.push(props)
        return () => h('div', { 'data-testid': 'roles-layout' }, [
          h('div', { 'data-testid': 'layout-state' }, `${props.loading}|${props.error}|${props.disableCreate}`),
          h('div', { 'data-testid': 'layout-rows' }, JSON.stringify(props.rows)),
          slots.filters?.(),
          h('button', { type: 'button', onClick: () => emit('back') }, 'Back stub'),
          h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh stub'),
          h('button', { type: 'button', onClick: () => emit('create') }, 'Create stub'),
          h('button', { type: 'button', onClick: () => emit('rowActivate', 'role / special') }, 'Open role stub'),
        ])
      },
    }),
  }
})

vi.mock('../../../../src/ngb/metadata/NgbRecycleBinFilter.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      emits: ['update:modelValue'],
      setup(_, { emit }) {
        return () => h('button', { type: 'button', onClick: () => emit('update:modelValue', 'all') }, 'Show all roles')
      },
    }),
  }
})

vi.mock('../../../../src/ngb/security/NgbAccessDeniedState.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      setup: () => () => h('div', { 'data-testid': 'access-denied-stub' }, 'Denied'),
    }),
  }
})

import { ApiError } from '../../../../src/ngb/api/http'
import NgbRolesPage from '../../../../src/ngb/security/NgbRolesPage.vue'

function accessProfile() {
  return {
    userId: 'viewer',
    authSubject: 'viewer',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [],
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  state.layouts.length = 0
  setActivePinia(createPinia())
  state.getCurrentAccess.mockResolvedValue(accessProfile())
})

test('covers all filters, row/column formatters, refresh, and navigation actions', async () => {
  await page.viewport(1280, 900)
  state.getRoles.mockResolvedValue([
    {
      roleId: 'role-z', code: 'z-role', name: 'Zulu', description: '',
      isSystem: false, isActive: false, assignedUsersCount: 'invalid',
    },
    {
      roleId: 'role-a', code: 'a-role', name: 'Alpha', description: 'Primary role',
      isSystem: true, isActive: true, assignedUsersCount: 3.8,
    },
  ])

  const view = await render(NgbRolesPage)
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('Alpha')
  await expect.element(view.getByTestId('layout-state')).toHaveTextContent('false|null|true')

  const columns = state.layouts[0]!.columns as Array<{ format?: (value: unknown) => string }>
  expect(columns[0]!.format?.(['Name', 'code'])).toBe('Name\ncode')
  expect(columns[0]!.format?.(null)).toBe('')
  expect(columns[2]!.format?.(3.8)).toBe('3')
  expect(columns[2]!.format?.('invalid')).toBe('0')

  await view.getByText('Show all roles').click()
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('Zulu')

  await view.getByText('Create stub').click()
  await view.getByText('Back stub').click()
  await view.getByText('Open role stub').click()
  expect(state.routerPush).toHaveBeenCalledWith('/admin/security/roles/new')
  expect(state.routerPush).toHaveBeenCalledWith('/home')
  expect(state.routerPush).toHaveBeenCalledWith('/admin/security/roles/role%20%2F%20special')

  state.getRoles.mockResolvedValueOnce([])
  await view.getByText('Refresh stub').click()
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('[]')
})

test('renders access denied only for 403 and a normal error for other failures', async () => {
  await page.viewport(1280, 900)
  state.getRoles.mockRejectedValueOnce(new ApiError({ message: 'Forbidden', status: 403, url: '/roles' }))
  const denied = await render(NgbRolesPage)
  await expect.element(denied.getByTestId('access-denied-stub')).toBeVisible()

  setActivePinia(createPinia())
  state.getRoles.mockRejectedValueOnce(new ApiError({ message: 'Unauthorized', status: 401, url: '/roles' }))
  const unauthorized = await render(NgbRolesPage)
  await expect.element(unauthorized.getByTestId('layout-state')).toHaveTextContent('false|Unauthorized|true')

  setActivePinia(createPinia())
  state.getRoles.mockRejectedValueOnce('offline')
  const failed = await render(NgbRolesPage)
  await expect.element(failed.getByTestId('layout-state').last()).toHaveTextContent('false|offline|true')
})
