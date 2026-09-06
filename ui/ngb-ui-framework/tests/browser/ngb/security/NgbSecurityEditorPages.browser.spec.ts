import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { mount } from '@vue/test-utils'
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

vi.mock('../../../../src/ngb/editor/NgbEntityAuditSidebar.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
    name: 'StubSecurityAuditSidebar',
    props: ['open', 'entityKind', 'entityId', 'entityTitle'],
    emits: ['back', 'close'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'security-audit-sidebar' }, [
        `Audit ${props.entityKind} ${props.entityId} ${props.entityTitle}`,
        h('button', { type: 'button', onClick: () => emit('back') }, 'Audit back'),
        h('button', { type: 'button', onClick: () => emit('close') }, 'Audit close'),
      ])
    },
  }),
  }
})

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
  vi.resetAllMocks()
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
  await view.getByRole('button', { name: 'Audit back' }).click()
  await view.getByTitle('Audit log').click()
  await view.getByRole('button', { name: 'Audit close' }).click()
  await view.getByTitle('Audit log').click()
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  await expect.element(view.getByTestId('security-audit-sidebar')).not.toBeInTheDocument()
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
  await view.getByRole('switch', { name: 'Require password update' }).click()
  await view.getByText('PM AP Clerk', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()

  expect(mocks.createUser).toHaveBeenCalledWith({
    email: 'clerk@example.com',
    firstName: null,
    lastName: null,
    displayName: 'Clerk One',
    enabled: true,
    temporaryPassword: 'Ngb#2026-Strong',
    requirePasswordUpdate: false,
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

  await view.getByRole('button', { name: 'Deactivate' }).first().click()
  await expect.element(view.getByText('Deactivate user?', { exact: true })).toBeVisible()
  expect(mocks.deactivateUser).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Cancel' }).click()
  await view.getByRole('button', { name: 'Deactivate' }).first().click()
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

test('handles user access denial, forbidden and ordinary load failures, and a missing route id', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'viewer',
    authSubject: 'viewer',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }],
  })
  const deniedNew = await render(NgbUserEditorPage)
  await expect.element(deniedNew.getByText('Access denied')).toBeVisible()
  expect(mocks.getRoles).not.toHaveBeenCalled()
  deniedNew.unmount()

  setActivePinia(createPinia())
  mocks.routeParams.userId = 'user-forbidden'
  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'admin', authSubject: 'admin', isAuthenticated: true, isActive: true,
    isBootstrapAdmin: false, accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' }],
  })
  mocks.getRoles.mockRejectedValueOnce(new ApiError({
    message: 'Forbidden', status: 403, url: '/api/security/roles', body: null,
  }))
  const forbidden = await render(NgbUserEditorPage)
  await expect.element(forbidden.getByText('Access denied')).toBeVisible()
  forbidden.unmount()

  setActivePinia(createPinia())
  mocks.getRoles.mockRejectedValueOnce(new Error('users unavailable'))
  const failed = await render(NgbUserEditorPage)
  await expect.element(failed.getByText('users unavailable')).toBeVisible()
  failed.unmount()

  setActivePinia(createPinia())
  delete mocks.routeParams.userId
  mocks.getRoles.mockResolvedValue([])
  const missing = await render(NgbUserEditorPage)
  await expect.element(missing.getByText('New user')).toBeVisible()
  missing.unmount()
})

test('uses title and actor fallbacks, keeps selected inactive roles, reports effective access failures, and navigates back', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([
    roleListItem({ roleId: 'role-inactive-selected', name: 'Selected inactive role', isActive: false, isSystem: false }),
    roleListItem({ roleId: 'role-inactive-hidden', name: 'Hidden inactive role', isActive: false, isSystem: false }),
  ])
  mocks.getUser.mockResolvedValue(userDetails({
    email: 'fallback@example.com',
    displayName: null,
    roles: [{
      roleId: 'role-inactive-selected',
      code: 'inactive-selected',
      name: 'Selected inactive role',
      isSystem: false,
      isActive: false,
    }],
  }))
  mocks.getUserEffectiveAccess.mockRejectedValue(new Error('effective access unavailable'))

  const view = await render(NgbUserEditorPage)

  await expect.element(view.getByRole('heading', { name: 'fallback@example.com' })).toBeVisible()
  await expect.element(view.getByText('Selected inactive role')).toBeVisible()
  expect(document.body.textContent).not.toContain('Hidden inactive role')
  await expect.element(view.getByText('Inactive', { exact: true })).toBeVisible()
  await expect.element(view.getByText('effective access unavailable')).toBeVisible()
  await view.getByRole('button', { name: 'Back' }).click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/admin/security/users')
  view.unmount()

  setActivePinia(createPinia())
  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue(userDetails({ email: null, displayName: null, roles: [] }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  const generic = await render(NgbUserEditorPage)
  await expect.element(generic.getByRole('heading', { name: 'User' })).toBeVisible()
  generic.unmount()
})

test('cancels password changes, toggles both password fields, removes a role, and uses active-state fallback on update', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([roleListItem()])
  mocks.getUser.mockResolvedValue(userDetails({ keycloakEnabled: null, isActive: false }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  mocks.updateUser.mockResolvedValue(userDetails({ keycloakEnabled: null, isActive: false, roles: [] }))

  const view = await render(NgbUserEditorPage)

  await view.getByRole('button', { name: 'Change password' }).click()
  await setInputValue('Password', 'temporary')
  await setInputValue('Confirm password', 'temporary')
  const showButtons = Array.from(document.querySelectorAll<HTMLButtonElement>('button[title="Show password"]'))
  expect(showButtons).toHaveLength(2)
  showButtons.forEach((button) => button.click())
  expect((await inputByLabel('Password')).type).toBe('text')
  expect((await inputByLabel('Confirm password')).type).toBe('text')
  await view.getByRole('button', { name: 'Cancel' }).click()
  expect(document.body.textContent).not.toContain('Password')

  await view.getByText('PM Administrator', { exact: true }).click()
  await view.getByRole('button', { name: 'Save' }).click()
  expect(mocks.updateUser).toHaveBeenCalledWith('user-1', expect.objectContaining({
    enabled: false,
    temporaryPassword: null,
    roleIds: [],
  }))
})

test('maps local, API, Keycloak, and ordinary save failures without duplicate messages', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.userId = 'new'
  mocks.getRoles.mockResolvedValue([])
  mocks.createUser
    .mockRejectedValueOnce(new ApiError({
      message: 'Validation failed',
      status: 400,
      url: '/api/security/users',
      body: {
        issues: [
          { path: 'email', message: 'Not a valid email', scope: 'field' },
          { path: 'profile', message: 'Profile is incomplete', scope: 'field' },
        ],
        errors: {
          email: ['Not a valid email'],
          password: ['Weak password policy'],
        },
      },
    }))
    .mockRejectedValueOnce(keycloakApiError('User already exists', 409))
    .mockRejectedValueOnce(keycloakApiError('Email is invalid'))
    .mockRejectedValueOnce(new ApiError({
      message: 'Identity provider failure',
      status: 502,
      url: '/api/security/users',
      body: { errorCode: 'ngb.keycloak.admin_request_failed' },
    }))
    .mockRejectedValueOnce(new ApiError({
      message: 'plain API failure',
      status: 500,
      url: '/api/security/users',
      body: null,
    }))
    .mockRejectedValueOnce(new Error('ordinary create failure'))

  const view = await render(NgbUserEditorPage)

  await setInputValue('Email', 'invalid-email')
  await setInputValue('Display name', 'Boundary User')
  await setInputValue('Password', 'Strong#2026')
  await setInputValue('Confirm password', 'Strong#2026')
  await view.getByRole('button', { name: 'Save' }).click()
  await expect.poll(() => document.body.textContent ?? '').toContain('Enter a valid email address.')
  expect(mocks.createUser).not.toHaveBeenCalled()

  await setInputValue('Email', 'boundary@example.com')
  await view.getByRole('button', { name: 'Save' }).click()
  await expect.element(view.getByText('Profile is incomplete', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Password does not meet the password policy.', { exact: true })).toBeVisible()
  expect(document.body.textContent?.match(/Enter a valid email address\./g)).toHaveLength(1)

  const expectedMessages = [
    'A user with this email already exists.',
    'Enter a valid email address.',
    'The identity provider rejected the user data. Check the email and password, then try again.',
    'plain API failure',
    'ordinary create failure',
  ]
  for (const expectedMessage of expectedMessages) {
    await view.getByRole('button', { name: 'Save' }).click()
    await expect.element(view.getByText(expectedMessage, { exact: true })).toBeVisible()
  }
})

test('reports activation failure and prevents a concurrent duplicate user save', async () => {
  await page.viewport(1280, 900)

  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue(userDetails({ roles: [] }))
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  mocks.deactivateUser.mockRejectedValueOnce(new Error('user deactivation failed'))

  const view = await render(NgbUserEditorPage)
  await view.getByRole('button', { name: 'Deactivate' }).click()
  await view.getByRole('button', { name: 'Deactivate', exact: true }).click()
  await expect.element(view.getByText('user deactivation failed')).toBeVisible()
  await view.getByRole('button', { name: 'Cancel' }).click()

  let resolveUpdate!: (value: UserDetailsDto) => void
  mocks.updateUser.mockReturnValue(new Promise((resolve) => { resolveUpdate = resolve }))
  const save = view.getByRole('button', { name: 'Save' }).element() as HTMLButtonElement
  save.click()
  save.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.updateUser).toHaveBeenCalledTimes(1)
  resolveUpdate(userDetails({ roles: [] }))
  await expect.poll(() => save.disabled).toBe(false)
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
  await view.getByRole('button', { name: 'Cancel' }).click()
  await view.getByRole('button', { name: 'Deactivate', exact: true }).first().click()
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

test('handles new-role access denial, forbidden loads, ordinary load failures, and a missing route id', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.roleId = 'new'
  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'viewer',
    authSubject: 'viewer',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'roles', actionCode: 'view' }],
  })
  const deniedNew = await render(NgbRoleEditorPage)
  await expect.element(deniedNew.getByText('Access denied')).toBeVisible()
  expect(mocks.getPermissionDefinitions).not.toHaveBeenCalled()
  deniedNew.unmount()

  setActivePinia(createPinia())
  mocks.routeParams.roleId = 'role-forbidden'
  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'admin', authSubject: 'admin', isAuthenticated: true, isActive: true,
    isBootstrapAdmin: false, accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' }],
  })
  mocks.getPermissionDefinitions.mockRejectedValueOnce(new ApiError({
    message: 'Forbidden', status: 403, url: '/api/security/permissions', body: null,
  }))
  const forbidden = await render(NgbRoleEditorPage)
  await expect.element(forbidden.getByText('Access denied')).toBeVisible()
  forbidden.unmount()

  setActivePinia(createPinia())
  mocks.getPermissionDefinitions.mockRejectedValueOnce(new Error('metadata unavailable'))
  const failed = await render(NgbRoleEditorPage)
  await expect.element(failed.getByText('metadata unavailable')).toBeVisible()
  failed.unmount()

  setActivePinia(createPinia())
  delete mocks.routeParams.roleId
  mocks.getPermissionDefinitions.mockResolvedValue([])
  const missing = await render(NgbRoleEditorPage)
  await expect.element(missing.getByText('New role')).toBeVisible()
  missing.unmount()
})

test('covers loading, back navigation, audit close events, and assigned-user display fallbacks', async () => {
  await page.viewport(1280, 900)

  let resolveRole!: (value: RoleDetailsDto) => void
  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.getRole.mockReturnValue(new Promise((resolve) => { resolveRole = resolve }))
  const view = await render(NgbRoleEditorPage)
  await expect.element(view.getByText('Loading...')).toBeVisible()
  expect(document.querySelector('h1')?.textContent).toBe('Role')

  resolveRole(roleDetails({
    description: null,
    isSystem: false,
    assignedUsers: [
      { userId: 'user-email', email: 'fallback@example.com', displayName: '', isActive: false },
      { userId: 'user-id', email: '', displayName: '', isActive: true },
    ],
  }))
  await expect.element(view.getByText('PM AR Clerk')).toBeVisible()
  await view.getByRole('button', { name: 'Back' }).click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/admin/security/roles')

  await view.getByTitle('Audit log').click()
  await expect.element(view.getByTestId('security-audit-sidebar')).toBeVisible()
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  await expect.element(view.getByTestId('security-audit-sidebar')).not.toBeInTheDocument()
  await view.getByTitle('Audit log').click()
  await view.getByRole('button', { name: 'Audit back' }).click()
  await view.getByTitle('Audit log').click()
  await view.getByRole('button', { name: 'Audit close' }).click()

  await view.getByRole('tab', { name: 'Assigned users' }).click()
  await expect.element(view.getByText('fallback@example.com').first()).toBeVisible()
  await expect.element(view.getByText('user-id')).toBeVisible()
  await expect.element(view.getByText('Inactive')).toBeVisible()
  view.unmount()

  mocks.getRole.mockResolvedValue(roleDetails({ assignedUsers: [] }))
  const empty = await render(NgbRoleEditorPage)
  await empty.getByRole('tab', { name: 'Assigned users' }).click()
  await expect.element(empty.getByText('No assigned users.')).toBeVisible()
  empty.unmount()
})

test('reports create, update, and activation failures and prevents a concurrent duplicate save', async () => {
  await page.viewport(1280, 900)

  mocks.routeParams.roleId = 'new'
  mocks.getPermissionDefinitions.mockResolvedValue([])
  mocks.createRole.mockRejectedValue(new Error('create failed'))
  const create = await render(NgbRoleEditorPage)
  await setInputValue('Code', 'custom-role')
  await setInputValue('Name', 'Custom Role')
  await create.getByRole('button', { name: 'Save' }).click()
  await expect.element(create.getByText('create failed')).toBeVisible()
  expect(mocks.createRole).toHaveBeenCalledWith(expect.objectContaining({ description: null }))
  create.unmount()

  mocks.routeParams.roleId = 'role-1'
  mocks.getRole.mockResolvedValue(roleDetails({ description: null, isActive: false }))
  mocks.updateRole.mockRejectedValueOnce(new Error('update failed'))
  const update = await render(NgbRoleEditorPage)
  await update.getByRole('button', { name: 'Save' }).click()
  await expect.element(update.getByText('update failed')).toBeVisible()
  expect(mocks.updateRole).toHaveBeenCalledWith('role-1', expect.objectContaining({ description: null, isActive: false }))
  update.unmount()

  mocks.getRole.mockResolvedValue(roleDetails())
  mocks.deactivateRole.mockRejectedValue(new Error('deactivate failed'))
  const activation = await render(NgbRoleEditorPage)
  await activation.getByRole('button', { name: 'Deactivate' }).click()
  await activation.getByRole('button', { name: 'Deactivate', exact: true }).click()
  await expect.element(activation.getByText('deactivate failed')).toBeVisible()
  activation.unmount()

  let resolveUpdate!: (value: RoleDetailsDto) => void
  mocks.updateRole.mockReturnValue(new Promise((resolve) => { resolveUpdate = resolve }))
  const concurrent = await render(NgbRoleEditorPage)
  const save = concurrent.getByRole('button', { name: 'Save' }).element() as HTMLButtonElement
  save.click()
  save.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.updateRole).toHaveBeenCalledTimes(1)
  resolveUpdate(roleDetails())
  await expect.poll(() => save.disabled).toBe(false)
  concurrent.unmount()
})

test('role editor discards every load phase that settles after unmount', async () => {
  await page.viewport(1280, 900)
  const adminAccess = {
    userId: 'admin', authSubject: 'admin', isAuthenticated: true, isActive: true,
    isBootstrapAdmin: false, accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' }],
  }

  let resolveAccess!: (value: typeof adminAccess) => void
  mocks.getCurrentAccess.mockReturnValueOnce(new Promise((resolve) => { resolveAccess = resolve }))
  const accessPending = await render(NgbRoleEditorPage)
  await vi.waitFor(() => expect(mocks.getCurrentAccess).toHaveBeenCalledOnce())
  accessPending.unmount()
  resolveAccess(adminAccess)
  await Promise.resolve()

  setActivePinia(createPinia())
  mocks.getCurrentAccess.mockResolvedValue(adminAccess)
  mocks.getRole.mockResolvedValue(roleDetails())
  let resolveDefinitions!: (value: PermissionDefinitionDto[]) => void
  mocks.getPermissionDefinitions.mockReturnValueOnce(new Promise((resolve) => { resolveDefinitions = resolve }))
  const dataPending = await render(NgbRoleEditorPage)
  await vi.waitFor(() => expect(mocks.getPermissionDefinitions).toHaveBeenCalled())
  dataPending.unmount()
  resolveDefinitions([])
  await Promise.resolve()

  setActivePinia(createPinia())
  let rejectDefinitions!: (cause: unknown) => void
  mocks.getPermissionDefinitions.mockReturnValueOnce(new Promise((_resolve, reject) => { rejectDefinitions = reject }))
  const failurePending = await render(NgbRoleEditorPage)
  await vi.waitFor(() => expect(mocks.getPermissionDefinitions).toHaveBeenCalledTimes(2))
  failurePending.unmount()
  rejectDefinitions(new Error('late role metadata failure'))
  await Promise.resolve()
})

test('user editor discards access, data, and failure results that settle after unmount', async () => {
  await page.viewport(1280, 900)
  const adminAccess = {
    userId: 'admin', authSubject: 'admin', isAuthenticated: true, isActive: true,
    isBootstrapAdmin: false, accessVersion: 1,
    permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' }],
  }

  let resolveAccess!: (value: typeof adminAccess) => void
  mocks.getCurrentAccess.mockReturnValueOnce(new Promise((resolve) => { resolveAccess = resolve }))
  const accessPending = await render(NgbUserEditorPage)
  await vi.waitFor(() => expect(mocks.getCurrentAccess).toHaveBeenCalledOnce())
  accessPending.unmount()
  resolveAccess(adminAccess)
  await Promise.resolve()

  setActivePinia(createPinia())
  mocks.getCurrentAccess.mockResolvedValue(adminAccess)
  mocks.getUser.mockResolvedValue(userDetails())
  mocks.getUserEffectiveAccess.mockResolvedValue(effectiveAccess())
  let resolveRoles!: (value: RoleListItemDto[]) => void
  mocks.getRoles.mockReturnValueOnce(new Promise((resolve) => { resolveRoles = resolve }))
  const dataPending = await render(NgbUserEditorPage)
  await vi.waitFor(() => expect(mocks.getRoles).toHaveBeenCalled())
  dataPending.unmount()
  resolveRoles([])
  await Promise.resolve()

  setActivePinia(createPinia())
  let rejectRoles!: (cause: unknown) => void
  mocks.getRoles.mockReturnValueOnce(new Promise((_resolve, reject) => { rejectRoles = reject }))
  const failurePending = await render(NgbUserEditorPage)
  await vi.waitFor(() => expect(mocks.getRoles).toHaveBeenCalledTimes(2))
  failurePending.unmount()
  rejectRoles(new Error('late user roles failure'))
  await Promise.resolve()
})

test('user editor ignores effective-access refreshes that settle after unmount', async () => {
  mocks.getRoles.mockResolvedValue([])
  mocks.getUser.mockResolvedValue(userDetails({ roles: [] }))
  mocks.getUserEffectiveAccess.mockResolvedValueOnce(effectiveAccess())

  const successful = mount(NgbUserEditorPage, { attachTo: document.body })
  const successfulState = (successful.vm as any).$?.setupState
  await vi.waitFor(() => expect(successfulState.user).toBeTruthy())

  mocks.getUserEffectiveAccess.mockRejectedValueOnce('refreshed effective access unavailable')
  await successfulState.loadEffectiveAccess()
  expect(successfulState.effectiveAccess).toBeNull()
  expect(successfulState.effectiveError).toBe('refreshed effective access unavailable')

  let resolveEffective!: (value: ReturnType<typeof effectiveAccess>) => void
  mocks.getUserEffectiveAccess.mockImplementationOnce(() => new Promise((resolve) => {
    resolveEffective = resolve
  }))
  const successfulRequest = successfulState.loadEffectiveAccess()
  await vi.waitFor(() => expect(mocks.getUserEffectiveAccess).toHaveBeenCalledTimes(3))
  successful.unmount()
  resolveEffective(effectiveAccess())
  await successfulRequest

  setActivePinia(createPinia())
  mocks.getUserEffectiveAccess.mockResolvedValueOnce(effectiveAccess())
  const failed = mount(NgbUserEditorPage, { attachTo: document.body })
  const failedState = (failed.vm as any).$?.setupState
  await vi.waitFor(() => expect(failedState.user).toBeTruthy())
  let rejectEffective!: (cause: unknown) => void
  mocks.getUserEffectiveAccess.mockImplementationOnce(() => new Promise((_resolve, reject) => {
    rejectEffective = reject
  }))
  const failedRequest = failedState.loadEffectiveAccess()
  await vi.waitFor(() => expect(mocks.getUserEffectiveAccess).toHaveBeenCalledTimes(5))
  failed.unmount()
  rejectEffective(new Error('late effective access failure'))
  await failedRequest
})
