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

vi.mock('../../../../src/ngb/editor/NgbEntityAuditSidebar.vue', () => ({
  default: {
    name: 'StubSecurityAuditSidebar',
    props: ['open', 'entityKind', 'entityId', 'entityTitle'],
    template: '<div data-testid="security-audit-sidebar">Audit {{ entityKind }} {{ entityId }} {{ entityTitle }}</div>',
  },
}))

import NgbUserEditorPage from '../../../../src/ngb/security/NgbUserEditorPage.vue'
import NgbRoleEditorPage from '../../../../src/ngb/security/NgbRoleEditorPage.vue'
import { ApiError } from '../../../../src/ngb/api/http'
import type {
  PermissionDefinitionDto,
  RoleDetailsDto,
  RoleListItemDto,
  UserDetailsDto,
} from '../../../../src/ngb/security/types'

function roleListItem(overrides: Partial<RoleListItemDto> = {}): RoleListItemDto {
  return {
    roleId: 'role-1',
    code: 'pm-administrator',
    name: 'PM Administrator',
    description: 'Full PM access',
    isSystem: true,
    isActive: true,
    assignedUsersCount: 1,
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
    ...overrides,
  }
}

function userDetails(overrides: Partial<UserDetailsDto> = {}): UserDetailsDto {
  return {
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
    ...overrides,
  }
}

function roleDetails(overrides: Partial<RoleDetailsDto> = {}): RoleDetailsDto {
  return {
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
    ...overrides,
  }
}

function permissionDefinition(overrides: Partial<PermissionDefinitionDto> = {}): PermissionDefinitionDto {
  return {
    resourceKind: 'system',
    resourceCode: 'users',
    actionCode: 'view',
    displayName: 'View users',
    group: 'System',
    ...overrides,
  }
}

function effectiveAccess(userId = 'user-1') {
  return { userId, accessVersion: 2, groups: [] }
}

function queryInputByLabel(label: string): HTMLInputElement | null {
  const labelElement = Array.from(document.querySelectorAll('label'))
    .find((element) => element.textContent?.trim() === label)
  const input = labelElement?.parentElement?.querySelector('input')
  return input instanceof HTMLInputElement ? input : null
}

async function inputByLabel(label: string): Promise<HTMLInputElement> {
  for (let attempt = 0; attempt < 50; attempt += 1) {
    const input = queryInputByLabel(label)
    if (input) return input
    await new Promise((resolve) => setTimeout(resolve, 10))
  }

  const labels = Array.from(document.querySelectorAll('label'))
    .map((element) => element.textContent?.trim())
    .filter(Boolean)
    .join(', ')
  throw new Error(`Input not found for label: ${label}. Available labels: ${labels}`)
}

async function setInputValue(label: string, value: string): Promise<void> {
  const input = await inputByLabel(label)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

function keycloakApiError(body: string, statusCode = 400): ApiError {
  return new ApiError({
    message: 'Keycloak request failed',
    status: statusCode,
    url: '/api/security/users',
    body: {
      errorCode: 'ngb.keycloak.admin_request_failed',
      context: {
        statusCode,
        keycloakErrorBody: body,
      },
    },
  })
}

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
    roleListItem({ description: null }),
  ])
  mocks.getUser.mockResolvedValue(userDetails())
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())

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

test('opens user audit log with the security user entity context', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([roleListItem()])
  mocks.getUser.mockResolvedValue(userDetails())
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())

  const view = await render(NgbUserEditorPage)

  await view.getByTitle('Audit log').click()

  await expect.element(view.getByTestId('security-audit-sidebar')).toHaveTextContent('Audit 8 user-1 Alex Carter')
})

test('requires password fields only after change password is selected for an existing user', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue(userDetails({ roles: [] }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())

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

test('saves existing user profile and role changes without sending a password unless change password is active', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([
    roleListItem(),
    roleListItem({
      roleId: 'role-2',
      code: 'pm-ap-clerk',
      name: 'PM AP Clerk',
      assignedUsersCount: 0,
    }),
  ])
  mocks.getUser.mockResolvedValue(userDetails())
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  mocks.updateUser.mockResolvedValue(userDetails({
    displayName: 'Alex C.',
    roles: [
      { roleId: 'role-1', code: 'pm-administrator', name: 'PM Administrator', isSystem: true, isActive: true },
      { roleId: 'role-2', code: 'pm-ap-clerk', name: 'PM AP Clerk', isSystem: true, isActive: true },
    ],
    accessVersion: 3,
  }))

  const view = await render(NgbUserEditorPage)

  await setInputValue('Display name', 'Alex C.')
  await view.getByText('PM AP Clerk', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.updateUser).toHaveBeenCalledWith('user-1', {
    email: 'alex.carter@demo.ngbplatform.com',
    firstName: null,
    lastName: null,
    displayName: 'Alex C.',
    enabled: true,
    temporaryPassword: null,
    requirePasswordUpdate: false,
    roleIds: ['role-1', 'role-2'],
  })
})

test('sends a password only from explicit change-password mode and then hides password fields after save', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue(userDetails({ roles: [] }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  mocks.updateUser.mockResolvedValue(userDetails({ roles: [], accessVersion: 3 }))

  const view = await render(NgbUserEditorPage)

  await view.getByRole('button', { name: 'Change password' }).click()
  await setInputValue('Password', 'Ngb#2026-Strong')
  await setInputValue('Confirm password', 'Ngb#2026-Strong')
  const showPasswordButton = document.querySelector<HTMLButtonElement>('button[title="Show password"]')
  expect(showPasswordButton).not.toBeNull()
  showPasswordButton?.click()
  await new Promise((resolve) => setTimeout(resolve, 0))
  expect((await inputByLabel('Password')).type).toBe('text')

  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.updateUser).toHaveBeenCalledWith('user-1', expect.objectContaining({
    temporaryPassword: 'Ngb#2026-Strong',
    requirePasswordUpdate: false,
  }))
  expect(document.body.textContent).not.toContain('Password')
})

test('maps Keycloak validation failures to user-friendly messages on new user save', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getRoles.mockResolvedValue([])
  mocks.createUser.mockRejectedValue(keycloakApiError('Password policy not met'))

  const view = await render(NgbUserEditorPage)

  await setInputValue('Email', 'clerk@example.com')
  await setInputValue('Display name', 'Clerk One')
  await setInputValue('Password', 'weak')
  await setInputValue('Confirm password', 'weak')
  await view.getByRole('button', { name: 'Save' }).click()

  await expect.element(view.getByText('Password does not meet the password policy.', { exact: true })).toBeVisible()
})

test('requires matching password confirmation before creating a user', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getRoles.mockResolvedValue([])

  const view = await render(NgbUserEditorPage)

  await setInputValue('Email', 'clerk@example.com')
  await setInputValue('Display name', 'Clerk One')
  await setInputValue('Password', 'Ngb#2026-Strong')
  await setInputValue('Confirm password', 'Ngb#2026-Different')
  await view.getByRole('button', { name: 'Save' }).click()

  expect(document.body.textContent).toContain('Passwords do not match.')
  expect(mocks.createUser).not.toHaveBeenCalled()
})

test('creates a user with selected application roles and no first/last name fields', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getRoles.mockResolvedValue([
    roleListItem({
      roleId: 'role-2',
      code: 'pm-ap-clerk',
      name: 'PM AP Clerk',
      assignedUsersCount: 0,
    }),
  ])
  mocks.createUser.mockResolvedValue(userDetails({
    userId: 'user-2',
    authSubject: 'kc-user-2',
    email: 'clerk@example.com',
    firstName: null,
    lastName: null,
    displayName: 'Clerk One',
    roles: [{ roleId: 'role-2', code: 'pm-ap-clerk', name: 'PM AP Clerk', isSystem: true, isActive: true }],
  }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess('user-2'))

  const view = await render(NgbUserEditorPage)

  await setInputValue('Email', 'clerk@example.com')
  await setInputValue('Display name', 'Clerk One')
  await setInputValue('Password', 'Ngb#2026-Strong')
  await setInputValue('Confirm password', 'Ngb#2026-Strong')
  await view.getByText('PM AP Clerk', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.createUser).toHaveBeenCalledWith({
    email: 'clerk@example.com',
    firstName: null,
    lastName: null,
    displayName: 'Clerk One',
    enabled: true,
    temporaryPassword: 'Ngb#2026-Strong',
    requirePasswordUpdate: true,
    roleIds: ['role-2'],
  })
  expect(mocks.routerReplace).toHaveBeenCalledWith('/admin/security/users/user-2')
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

test('deactivates and reactivates users only through confirmation actions', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([])
  mocks.getUser
    .mockResolvedValueOnce(userDetails({ roles: [] }))
    .mockResolvedValueOnce(userDetails({ roles: [], isActive: false, keycloakEnabled: false }))
    .mockResolvedValueOnce(userDetails({ roles: [], isActive: false, keycloakEnabled: false }))
    .mockResolvedValueOnce(userDetails({ roles: [], isActive: true, keycloakEnabled: true }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  mocks.deactivateUser.mockResolvedValue(undefined)
  mocks.reactivateUser.mockResolvedValue(undefined)

  const view = await render(NgbUserEditorPage)

  await view.getByRole('button', { name: 'Deactivate' }).click()
  await expect.element(view.getByText('Deactivate user?', { exact: true })).toBeVisible()
  expect(mocks.deactivateUser).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Deactivate', exact: true }).click()
  expect(mocks.deactivateUser).toHaveBeenCalledWith('user-1')
  await expect.element(view.getByRole('button', { name: 'Reactivate' })).toBeVisible()

  await view.getByRole('button', { name: 'Reactivate' }).click()
  await expect.element(view.getByText('Reactivate user?', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Reactivate', exact: true }).click()
  expect(mocks.reactivateUser).toHaveBeenCalledWith('user-1')
})

test('renders existing user as view-only when manage permission is missing', async () => {
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
  mocks.getRoles.mockResolvedValue([roleListItem()])
  mocks.getUser.mockResolvedValue(userDetails())
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())

  const view = await render(NgbUserEditorPage)

  expect((await inputByLabel('Email')).disabled).toBe(true)
  expect((await inputByLabel('Display name')).disabled).toBe(true)
  expect((view.getByRole('button', { name: 'Save' }).element() as HTMLButtonElement).disabled).toBe(true)
  expect(document.body.textContent).not.toContain('Deactivate')
  const roleCheckbox = document.querySelector('section input[type="checkbox"]') as HTMLInputElement | null
  expect(roleCheckbox?.disabled).toBe(true)
})

test('renders existing role editor without header role code or active switch', async () => {
  await page.viewport(1280, 900)

  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.getRole.mockResolvedValue(roleDetails())

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

test('opens role audit log with the security role entity context', async () => {
  await page.viewport(1280, 900)

  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.getRole.mockResolvedValue(roleDetails())

  const view = await render(NgbRoleEditorPage)

  await view.getByTitle('Audit log').click()

  await expect.element(view.getByTestId('security-audit-sidebar')).toHaveTextContent('Audit 9 role-1 PM AR Clerk')
})

test('saves role permission changes from the permissions tab with active status preserved', async () => {
  await page.viewport(1280, 900)

  mocks.getPermissionDefinitions.mockResolvedValue([
    permissionDefinition(),
    permissionDefinition({
      resourceKind: 'system',
      resourceCode: 'roles',
      actionCode: 'manage',
      displayName: 'Manage roles',
    }),
  ])
  mocks.getRole.mockResolvedValue(roleDetails({
    permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }],
  }))
  mocks.updateRole.mockResolvedValue(roleDetails({
    permissions: [
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' },
    ],
  }))

  const view = await render(NgbRoleEditorPage)

  await view.getByText('View users', { exact: true }).click()
  await view.getByText('Manage roles', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.updateRole).toHaveBeenCalledWith('role-1', {
    code: 'pm-ar-clerk',
    name: 'PM AR Clerk',
    description: 'Receivables document and receivables report access.',
    isActive: true,
    permissions: [
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' },
    ],
  })
})

test('creates a new role from selected permissions and hides assigned users until the role exists', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.roleId = 'new'
  mocks.getPermissionDefinitions.mockResolvedValue([
    permissionDefinition({
      resourceKind: 'report',
      resourceCode: 'pm.occupancy.summary',
      actionCode: 'execute',
      displayName: 'Occupancy Summary: Execute',
      group: 'Reports',
    }),
  ])
  mocks.createRole.mockResolvedValue(roleDetails({
    roleId: 'role-2',
    code: 'pm-report-runner',
    name: 'PM Report Runner',
    description: 'Can run selected reports.',
    isSystem: false,
    permissions: [{ resourceKind: 'report', resourceCode: 'pm.occupancy.summary', actionCode: 'execute' }],
    assignedUsers: [],
  }))

  const view = await render(NgbRoleEditorPage)

  await expect.element(view.getByText('New role')).toBeVisible()
  expect(document.body.textContent).not.toContain('Assigned users')
  await setInputValue('Code', 'pm-report-runner')
  await setInputValue('Name', 'PM Report Runner')
  await setInputValue('Description', 'Can run selected reports.')
  await view.getByText('Occupancy Summary: Execute', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.createRole).toHaveBeenCalledWith({
    code: 'pm-report-runner',
    name: 'PM Report Runner',
    description: 'Can run selected reports.',
    permissions: [{ resourceKind: 'report', resourceCode: 'pm.occupancy.summary', actionCode: 'execute' }],
  })
  expect(mocks.routerReplace).toHaveBeenCalledWith('/admin/security/roles/role-2')
})

test('deactivates and reactivates roles only through confirmation actions', async () => {
  await page.viewport(1280, 900)

  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.getRole
    .mockResolvedValueOnce(roleDetails())
    .mockResolvedValueOnce(roleDetails({ isActive: false }))
    .mockResolvedValueOnce(roleDetails({ isActive: false }))
    .mockResolvedValueOnce(roleDetails({ isActive: true }))
  mocks.deactivateRole.mockResolvedValue(undefined)
  mocks.reactivateRole.mockResolvedValue(undefined)

  const view = await render(NgbRoleEditorPage)

  await view.getByRole('button', { name: 'Deactivate' }).click()
  await expect.element(view.getByText('Deactivate role?', { exact: true })).toBeVisible()
  expect(mocks.deactivateRole).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Deactivate', exact: true }).click()
  expect(mocks.deactivateRole).toHaveBeenCalledWith('role-1')
  await expect.element(view.getByRole('button', { name: 'Reactivate' })).toBeVisible()

  await view.getByRole('button', { name: 'Reactivate' }).click()
  await expect.element(view.getByText('Reactivate role?', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Reactivate', exact: true }).click()
  expect(mocks.reactivateRole).toHaveBeenCalledWith('role-1')
})

test('renders existing role as view-only when manage permission is missing', async () => {
  await page.viewport(1280, 900)

  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'viewer',
    authSubject: 'viewer',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'view' },
    ],
  })
  mocks.getPermissionDefinitions.mockResolvedValue([permissionDefinition()])
  mocks.getRole.mockResolvedValue(roleDetails({
    permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }],
  }))

  const view = await render(NgbRoleEditorPage)

  expect((await inputByLabel('Code')).disabled).toBe(true)
  expect((await inputByLabel('Name')).disabled).toBe(true)
  expect((await inputByLabel('Description')).disabled).toBe(true)
  expect((view.getByRole('button', { name: 'Save' }).element() as HTMLButtonElement).disabled).toBe(true)
  expect(document.body.textContent).not.toContain('Deactivate')
  const permissionCheckbox = document.querySelector('[data-testid="permission-matrix"] input[type="checkbox"]') as HTMLInputElement | null
  expect(permissionCheckbox?.disabled).toBe(true)
})
