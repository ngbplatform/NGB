import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia, setActivePinia } from 'pinia'

const mocks = vi.hoisted(() => ({
  routeParams: { userId: 'user-1', roleId: 'role-1' } as Record<string, string>,
  routerPush: vi.fn(),
  routerReplace: vi.fn(),
  getCurrentAccess: vi.fn(),
  getRoles: vi.fn(),
  getUser: vi.fn(),
  getUserEffectiveAccess: vi.fn(),
  getPermissionDefinitions: vi.fn(),
  getRole: vi.fn(),
  createUser: vi.fn(),
  updateUser: vi.fn(),
  deactivateUser: vi.fn(),
  reactivateUser: vi.fn(),
  createRole: vi.fn(),
  updateRole: vi.fn(),
  deactivateRole: vi.fn(),
  reactivateRole: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: mocks.routeParams }),
  useRouter: () => ({ push: mocks.routerPush, replace: mocks.routerReplace }),
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: mocks.getCurrentAccess,
  getRoles: mocks.getRoles,
  getUser: mocks.getUser,
  getUserEffectiveAccess: mocks.getUserEffectiveAccess,
  getPermissionDefinitions: mocks.getPermissionDefinitions,
  getRole: mocks.getRole,
  createUser: mocks.createUser,
  updateUser: mocks.updateUser,
  deactivateUser: mocks.deactivateUser,
  reactivateUser: mocks.reactivateUser,
  createRole: mocks.createRole,
  updateRole: mocks.updateRole,
  deactivateRole: mocks.deactivateRole,
  reactivateRole: mocks.reactivateRole,
}))

import NgbUserEditorPage from '../../../../src/ngb/security/NgbUserEditorPage.vue'
import NgbRoleEditorPage from '../../../../src/ngb/security/NgbRoleEditorPage.vue'

beforeEach(() => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mocks.routeParams.userId = 'user-1'
  mocks.routeParams.roleId = 'role-1'
  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'admin',
    authSubject: 'admin',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' },
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' },
    ],
  })
})

test('renders existing user editor without header email or keycloak enabled switch', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([
    {
      roleId: 'role-1',
      code: 'pm-administrator',
      name: 'PM Administrator',
      description: null,
      isSystem: true,
      isActive: true,
      assignedUsersCount: 1,
      createdAtUtc: '2026-06-01T00:00:00Z',
      updatedAtUtc: '2026-06-01T00:00:00Z',
    },
  ])
  mocks.getUser.mockResolvedValue({
    userId: 'user-1',
    authSubject: 'kc-user-1',
    email: 'alex.carter@demo.ngbplatform.com',
    firstName: 'Alex',
    lastName: 'Carter',
    displayName: 'Alex Carter',
    isActive: true,
    keycloakEnabled: true,
    roles: [{ roleId: 'role-1', code: 'pm-administrator', name: 'PM Administrator', isSystem: true, isActive: true }],
    accessVersion: 2,
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
  })
  mocks.getUserEffectiveAccess.mockResolvedValue({ userId: 'user-1', accessVersion: 2, groups: [] })

  const view = await render(NgbUserEditorPage)

  await expect.element(view.getByText('Alex Carter')).toBeVisible()
  await expect.element(view.getByTitle('Audit log')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Deactivate' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Save' })).toBeVisible()
  expect(document.body.textContent).not.toContain('alex.carter@demo.ngbplatform.com')
  expect(document.body.textContent).not.toContain('Keycloak enabled')
  expect(document.body.textContent).not.toContain('First name')
  expect(document.body.textContent).not.toContain('Last name')
  expect(document.body.textContent).not.toContain('Password')
  expect(document.querySelector('[role="switch"]')).toBeNull()
  await expect.element(view.getByRole('button', { name: 'Change password' })).toBeVisible()

  await view.getByRole('button', { name: 'Open role PM Administrator' }).click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/admin/security/roles/role-1')
})

test('requires password fields only after change password is selected for an existing user', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue({
    userId: 'user-1',
    authSubject: 'kc-user-1',
    email: 'alex.carter@demo.ngbplatform.com',
    firstName: 'Alex',
    lastName: 'Carter',
    displayName: 'Alex Carter',
    isActive: true,
    keycloakEnabled: true,
    roles: [],
    accessVersion: 2,
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
  })
  mocks.getUserEffectiveAccess.mockResolvedValue({ userId: 'user-1', accessVersion: 2, groups: [] })

  const view = await render(NgbUserEditorPage)

  expect(document.body.textContent).not.toContain('Password')
  await view.getByRole('button', { name: 'Change password' }).click()
  await expect.element(view.getByText('Password', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Confirm password', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Save' }).click()

  expect(document.body.textContent).toContain('Password is required.')
  expect(document.body.textContent).toContain('Confirm password is required.')
  expect(mocks.updateUser).not.toHaveBeenCalled()
})

test('validates required new user fields before calling backend', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getRoles.mockResolvedValue([])

  const view = await render(NgbUserEditorPage)

  await expect.element(view.getByText('New user')).toBeVisible()
  await expect.element(view.getByText('Password', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Confirm password', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Save' }).click()

  expect(document.body.textContent).toContain('Email is required.')
  expect(document.body.textContent).toContain('Display name is required.')
  expect(document.body.textContent).toContain('Password is required.')
  expect(document.body.textContent).toContain('Confirm password is required.')
  expect(mocks.createUser).not.toHaveBeenCalled()
})

test('renders existing role editor without header role code or active switch', async () => {
  await page.viewport(1280, 900)

  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.getRole.mockResolvedValue({
    roleId: 'role-1',
    code: 'pm-ar-clerk',
    name: 'PM AR Clerk',
    description: 'Receivables document and receivables report access.',
    isSystem: true,
    isActive: true,
    permissions: [],
    assignedUsers: [
      { userId: 'user-1', email: 'clerk@example.com', displayName: 'Clerk One', isActive: true },
    ],
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
  })

  const view = await render(NgbRoleEditorPage)

  await expect.element(view.getByText('PM AR Clerk')).toBeVisible()
  await expect.element(view.getByTitle('Audit log')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Deactivate' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Save' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Permissions' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Assigned users' })).toBeVisible()
  expect(document.body.textContent).not.toContain('pm-ar-clerk')
  expect(document.querySelector('[role="switch"]')).toBeNull()

  await view.getByRole('tab', { name: 'Assigned users' }).click()
  await expect.element(view.getByText('Clerk One')).toBeVisible()
  await expect.element(view.getByText('clerk@example.com')).toBeVisible()
})
