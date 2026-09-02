import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia, setActivePinia } from 'pinia'

const state = vi.hoisted(() => ({
  getCurrentAccess: vi.fn(),
  getUsers: vi.fn(),
  routerPush: vi.fn(),
  layouts: [] as Array<Record<string, unknown>>,
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: state.getCurrentAccess,
  getUsers: state.getUsers,
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
        disablePrev: Boolean,
        disableNext: Boolean,
      },
      emits: ['back', 'refresh', 'create', 'prev', 'next', 'rowActivate'],
      setup(props, { emit, slots }) {
        state.layouts.push(props)
        return () => h('div', { 'data-testid': 'users-layout' }, [
          h('div', { 'data-testid': 'layout-state' }, `${props.loading}|${props.error}|${props.disableCreate}`),
          h('div', { 'data-testid': 'layout-rows' }, JSON.stringify(props.rows)),
          slots.filters?.(),
          h('button', { type: 'button', onClick: () => emit('back') }, 'Back stub'),
          h('button', { type: 'button', onClick: () => emit('refresh') }, 'Refresh stub'),
          h('button', { type: 'button', onClick: () => emit('create') }, 'Create stub'),
          h('button', { type: 'button', onClick: () => emit('prev') }, 'Previous stub'),
          h('button', { type: 'button', onClick: () => emit('next') }, 'Next stub'),
          h('button', { type: 'button', onClick: () => emit('rowActivate', 'user / special') }, 'Open user stub'),
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
        return () => h('div', [
          h('button', { type: 'button', onClick: () => emit('update:modelValue', 'deleted') }, 'Show deleted users'),
          h('button', { type: 'button', onClick: () => emit('update:modelValue', 'all') }, 'Show all users'),
        ])
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
import NgbUsersPage from '../../../../src/ngb/security/NgbUsersPage.vue'

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

test('covers user formatting, every status filter, refresh, and navigation actions', async () => {
  await page.viewport(1280, 900)
  state.getUsers.mockResolvedValue({ items: [
    {
      userId: 'user-empty', authSubject: 'subject-empty', email: null, displayName: null, isActive: true,
      keycloakEnabled: false, roles: [], createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-empty-2', authSubject: 'subject-empty-2', email: null, displayName: null, isActive: true,
      keycloakEnabled: false, roles: [], createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-email', authSubject: 'subject-email', email: 'email-only@example.test', displayName: null, isActive: true,
      keycloakEnabled: false, roles: [], createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-z', authSubject: 'subject-z', email: '', displayName: '', isActive: false,
      keycloakEnabled: false, roles: [], createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-b', authSubject: 'subject-b', email: 'beta@example.test', displayName: ' ', isActive: true,
      keycloakEnabled: null, roles: [], createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-a', authSubject: 'subject-a', email: 'alpha@example.test', displayName: ' Alpha ', isActive: true,
      keycloakEnabled: true,
      roles: [{ roleId: 'role-1', code: 'auditor', name: 'Auditor', isSystem: false, isActive: true }],
      createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    },
  ], offset: 0, limit: 100, total: 106 })

  const view = await render(NgbUsersPage)
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('Alpha')
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('beta@example.test')
  await expect.element(view.getByTestId('layout-state')).toHaveTextContent('false|null|true')

  const columns = state.layouts[0]!.columns as Array<{ format?: (value: unknown) => string }>
  expect(columns[0]!.format?.(['Name', 'email'])).toBe('Name\nemail')
  expect(columns[0]!.format?.(null)).toBe('')

  await view.getByText('Show deleted users').click()
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('subject-z')
  await view.getByText('Show all users').click()
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('Auditor')

  await view.getByText('Create stub').click()
  await view.getByText('Back stub').click()
  await view.getByText('Open user stub').click()
  await view.getByText('Next stub').click()
  expect(state.getUsers).toHaveBeenLastCalledWith(
    { offset: 100, limit: 100, isActive: null },
    { signal: expect.any(AbortSignal) },
  )
  await view.getByText('Previous stub').click()
  expect(state.getUsers).toHaveBeenLastCalledWith(
    { offset: 0, limit: 100, isActive: null },
    { signal: expect.any(AbortSignal) },
  )
  expect(state.routerPush).toHaveBeenCalledWith('/admin/security/users/new')
  expect(state.routerPush).toHaveBeenCalledWith('/home')
  expect(state.routerPush).toHaveBeenCalledWith('/admin/security/users/user%20%2F%20special')

  state.getUsers.mockResolvedValueOnce({ items: [], offset: 0, limit: 100, total: 0 })
  await view.getByText('Refresh stub').click()
  await expect.element(view.getByTestId('layout-rows')).toHaveTextContent('[]')
})

test('renders access denied only for 403 and reports other failures', async () => {
  await page.viewport(1280, 900)
  state.getUsers.mockRejectedValueOnce(new ApiError({ message: 'Forbidden', status: 403, url: '/users' }))
  const denied = await render(NgbUsersPage)
  await expect.element(denied.getByTestId('access-denied-stub')).toBeVisible()

  setActivePinia(createPinia())
  state.getUsers.mockRejectedValueOnce(new ApiError({ message: 'Unauthorized', status: 401, url: '/users' }))
  const unauthorized = await render(NgbUsersPage)
  await expect.element(unauthorized.getByTestId('layout-state')).toHaveTextContent('false|Unauthorized|true')

  setActivePinia(createPinia())
  state.getUsers.mockRejectedValueOnce('offline')
  const failed = await render(NgbUsersPage)
  await expect.element(failed.getByTestId('layout-state').last()).toHaveTextContent('false|offline|true')
})
