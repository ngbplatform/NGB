import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia, setActivePinia } from 'pinia'

const mocks = vi.hoisted(() => ({
  getCurrentAccess: vi.fn(),
  getUsers: vi.fn(),
  routerPush: vi.fn(),
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: mocks.getCurrentAccess,
  getUsers: mocks.getUsers,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mocks.routerPush }),
}))

import NgbUsersPage from '../../../../src/ngb/security/NgbUsersPage.vue'

beforeEach(() => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
})

test('renders users from backend-filtered security API and exposes create when manage permission exists', async () => {
  await page.viewport(1280, 900)

  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'admin',
    authSubject: 'admin',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' },
    ],
  })
  mocks.getUsers.mockResolvedValue([
    {
      userId: 'user-1',
      authSubject: 'kc-user-1',
      email: 'casey@example.test',
      displayName: 'Casey Morgan',
      isActive: true,
      keycloakEnabled: true,
      roles: [{ roleId: 'role-1', code: 'pm-auditor', name: 'PM Auditor', isSystem: true, isActive: true }],
      createdAtUtc: '2026-06-01T00:00:00Z',
      updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      userId: 'user-2',
      authSubject: 'deleted-kc-user',
      email: 'deleted@example.test',
      displayName: 'Deleted Keycloak User',
      isActive: true,
      keycloakEnabled: null,
      roles: [],
      createdAtUtc: '2026-06-01T00:00:00Z',
      updatedAtUtc: '2026-06-01T00:00:00Z',
    },
  ])

  const view = await render(NgbUsersPage)

  await expect.element(view.getByText('Casey Morgan')).toBeVisible()
  await expect.element(view.getByText('Deleted Keycloak User')).toBeVisible()
  await expect.element(view.getByText('PM Auditor')).toBeVisible()
  await expect.element(view.getByText('Yes', { exact: true })).toBeVisible()
  await expect.element(view.getByText('No', { exact: true })).toBeVisible()
  expect(document.querySelector('input[type="checkbox"][aria-label^="Keycloak:"]')).toBeNull()
  await expect.element(view.getByTitle('Create')).toBeVisible()
  expect((view.getByTitle('Create').element() as HTMLButtonElement).disabled).toBe(false)
  expect(document.querySelector('button[title="Delete"]')).toBeNull()

  await view.getByText('Casey Morgan').click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/admin/security/users/user-1')
})

test('disables create when access profile lacks manage permission', async () => {
  await page.viewport(1280, 900)

  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'viewer',
    authSubject: 'viewer',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
    ],
  })
  mocks.getUsers.mockResolvedValue([])

  const view = await render(NgbUsersPage)

  await expect.element(view.getByText('Users')).toBeVisible()
  await expect.element(view.getByTitle('Create')).toBeVisible()
  expect((view.getByTitle('Create').element() as HTMLButtonElement).disabled).toBe(true)
})

test('enables create for bootstrap admin even before explicit application roles are seeded', async () => {
  await page.viewport(1280, 900)

  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'bootstrap',
    authSubject: 'bootstrap',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: true,
    accessVersion: 1,
    permissions: [],
  })
  mocks.getUsers.mockResolvedValue([])

  const view = await render(NgbUsersPage)

  await expect.element(view.getByText('Users')).toBeVisible()
  expect((view.getByTitle('Create').element() as HTMLButtonElement).disabled).toBe(false)
})
