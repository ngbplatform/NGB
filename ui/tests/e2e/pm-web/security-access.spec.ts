import { expect, test, type Page, type Route } from '@playwright/test'

import {
  occupancySummaryReportDefinitionFixture,
  occupancySummaryReportExecutionFixture,
} from '../fixtures/pmReports'
import { expectNoHorizontalPageOverflow } from '../support/assertions'
import { rejectUnhandledApiRequests } from '../support/mockApi'

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}

async function fulfillEmpty(route: Route, status = 204): Promise<void> {
  await route.fulfill({
    status,
    body: '',
  })
}

function parseRequestJson<T>(route: Route): T | null {
  const raw = route.request().postData()
  if (!raw) return null

  try {
    return JSON.parse(raw) as T
  } catch {
    return null
  }
}

type SecurityPermission = {
  resourceKind: string
  resourceCode: string
  actionCode: string
}

type SecurityRole = {
  roleId: string
  code: string
  name: string
  isSystem: boolean
  isActive: boolean
}

type SecurityUser = {
  userId: string
  authSubject: string
  email: string
  displayName: string
  isActive: boolean
  keycloakEnabled: boolean
  roles: SecurityRole[]
}

const pmAdministratorRole: SecurityRole = {
  roleId: '22222222-2222-4222-8222-222222222221',
  code: 'pm-administrator',
  name: 'PM Administrator',
  isSystem: true,
  isActive: true,
}

const pmApClerkRole: SecurityRole = {
  roleId: '22222222-2222-4222-8222-222222222222',
  code: 'pm-ap-clerk',
  name: 'PM AP Clerk',
  isSystem: true,
  isActive: true,
}

const pmTestRole: SecurityRole = {
  roleId: '22222222-2222-4222-8222-222222222223',
  code: 'pm-test',
  name: 'PM Test',
  isSystem: false,
  isActive: true,
}

function userListItem(user: SecurityUser) {
  return {
    userId: user.userId,
    authSubject: user.authSubject,
    email: user.email,
    displayName: user.displayName,
    isActive: user.isActive,
    keycloakEnabled: user.keycloakEnabled,
    roles: user.roles,
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
  }
}

function userDetails(user: SecurityUser) {
  return {
    ...userListItem(user),
    firstName: null,
    lastName: null,
    accessVersion: user.isActive ? 2 : 3,
  }
}

function roleListItem(role: SecurityRole, assignedUsersCount = 0) {
  return {
    ...role,
    description: role.code === 'pm-ap-clerk'
      ? 'Payables document and operational payables access.'
      : 'Test report access.',
    assignedUsersCount,
    createdAtUtc: '2026-06-01T00:00:00Z',
    updatedAtUtc: '2026-06-01T00:00:00Z',
  }
}

function securityAccessResponse(
  roles: SecurityRole[],
  permissions: SecurityPermission[],
  isBootstrapAdmin = false,
) {
  return {
    userId: 'admin-user',
    authSubject: 'admin-user',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin,
    accessVersion: 1,
    roles,
    permissions,
  }
}

function inputByLabel(page: Page, label: string) {
  return page
    .locator('label')
    .filter({ hasText: new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`) })
    .locator('..')
    .locator('input')
    .first()
}

async function mockSecurityMenu(page: Page): Promise<void> {
  await page.route('**/api/main-menu', async (route) => {
    await fulfillJson(route, {
      groups: [
        {
          label: 'Setup & Controls',
          ordinal: 80,
          icon: 'cogs',
          items: [
            { kind: 'page', code: 'security-users', label: 'Users', route: '/admin/security/users', icon: 'users', ordinal: 10 },
            { kind: 'page', code: 'security-roles', label: 'Roles & Permissions', route: '/admin/security/roles', icon: 'shield', ordinal: 20 },
          ],
        },
      ],
    })
  })
}

async function mockReportOnlyMenu(page: Page): Promise<void> {
  await page.route('**/api/main-menu', async (route) => {
    await fulfillJson(route, {
      groups: [
        {
          label: 'Accounting',
          ordinal: 40,
          icon: 'bar-chart',
          items: [
            {
              kind: 'report',
              code: 'pm.occupancy.summary',
              label: 'Occupancy Summary',
              route: '/reports/pm.occupancy.summary',
              icon: 'bar-chart',
              ordinal: 10,
            },
          ],
        },
      ],
    })
  })
}

async function mockOccupancySummaryReportSurface(page: Page): Promise<void> {
  await page.route('**/api/report-definitions/pm.occupancy.summary', async (route) => {
    await fulfillJson(route, occupancySummaryReportDefinitionFixture)
  })

  await page.route('**/api/reports/pm.occupancy.summary/variants**', async (route) => {
    await fulfillJson(route, [])
  })

  await page.route('**/api/reports/pm.occupancy.summary/execute', async (route) => {
    await fulfillJson(route, occupancySummaryReportExecutionFixture)
  })
}

async function mockSecurityApis(page: Page, canManageUsers: boolean): Promise<void> {
  await page.route('**/api/security/me/access', async (route) => {
    await fulfillJson(
      route,
      securityAccessResponse(
        [pmAdministratorRole],
        [
        { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
        ...(canManageUsers ? [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' }] : []),
        ],
      ),
    )
  })

  await page.route('**/api/security/users', async (route) => {
    await fulfillJson(route, [
      {
        userId: '11111111-1111-4111-8111-111111111111',
        authSubject: 'kc-casey',
        email: 'casey@example.test',
        displayName: 'Casey Morgan',
        isActive: true,
        keycloakEnabled: true,
        roles: [
          { roleId: '22222222-2222-4222-8222-222222222222', code: 'pm-auditor', name: 'PM Auditor', isSystem: true, isActive: true },
        ],
        createdAtUtc: '2026-06-01T00:00:00Z',
        updatedAtUtc: '2026-06-01T00:00:00Z',
      },
    ])
  })

  await page.route('**/api/security/roles', async (route) => {
    await fulfillJson(route, [
      {
        roleId: '22222222-2222-4222-8222-222222222222',
        code: 'pm-auditor',
        name: 'PM Auditor',
        description: 'Read-only audit access',
        isSystem: true,
        isActive: true,
        assignedUsersCount: 1,
        createdAtUtc: '2026-06-01T00:00:00Z',
        updatedAtUtc: '2026-06-01T00:00:00Z',
      },
    ])
  })
}

async function mockReportOnlyAccess(page: Page): Promise<void> {
  await page.route('**/api/security/me/access', async (route) => {
    await fulfillJson(route, securityAccessResponse([pmTestRole], [
      { resourceKind: 'report', resourceCode: 'pm.occupancy.summary', actionCode: 'view' },
      { resourceKind: 'report', resourceCode: 'pm.occupancy.summary', actionCode: 'execute' },
    ]))
  })
}

async function mockForbiddenSecurityUsers(page: Page): Promise<void> {
  await page.route('**/api/security/users**', async (route) => {
    await fulfillJson(route, {
      title: 'Permission denied',
      detail: 'Permission denied.',
      status: 403,
    }, 403)
  })
}

async function mockSecurityManagementApis(page: Page): Promise<{
  getCreatedUsers: () => SecurityUser[]
}> {
  const roles = [pmAdministratorRole, pmApClerkRole, pmTestRole]
  let users: SecurityUser[] = []

  await page.route('**/api/security/me/access', async (route) => {
    await fulfillJson(route, securityAccessResponse([pmAdministratorRole], [
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' },
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'view' },
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' },
    ]))
  })

  await page.route('**/api/security/roles', async (route) => {
    await fulfillJson(route, roles.map((role) =>
      roleListItem(role, users.filter((user) => user.roles.some((assignedRole) => assignedRole.roleId === role.roleId)).length)))
  })

  await page.route('**/api/security/users**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())
    const method = request.method()

    if (pathname === '/api/security/users' && method === 'GET') {
      await fulfillJson(route, users.map(userListItem))
      return
    }

    if (pathname === '/api/security/users' && method === 'POST') {
      const payload = parseRequestJson<{
        email?: string
        displayName?: string | null
        roleIds?: string[]
      }>(route)
      const assignedRoles = roles.filter((role) => payload?.roleIds?.includes(role.roleId))
      const created: SecurityUser = {
        userId: '33333333-3333-4333-8333-333333333333',
        authSubject: 'kc-clerk-one',
        email: String(payload?.email ?? 'clerk@example.test'),
        displayName: String(payload?.displayName ?? payload?.email ?? 'Clerk One'),
        isActive: true,
        keycloakEnabled: true,
        roles: assignedRoles,
      }
      users = [created]
      await fulfillJson(route, userDetails(created), 201)
      return
    }

    const userMatch = pathname.match(/^\/api\/security\/users\/([^/]+)(?:\/([^/]+))?$/)
    if (!userMatch) {
      await route.fallback()
      return
    }

    const userId = decodeURIComponent(userMatch[1] ?? '')
    const action = userMatch[2] ? decodeURIComponent(userMatch[2]) : ''
    const user = users.find((entry) => entry.userId === userId) ?? null

    if (!user) {
      await fulfillJson(route, { title: 'User not found', status: 404 }, 404)
      return
    }

    if (method === 'GET' && action === '') {
      await fulfillJson(route, userDetails(user))
      return
    }

    if (method === 'GET' && action === 'effective-access') {
      await fulfillJson(route, {
        userId: user.userId,
        accessVersion: user.isActive ? 2 : 3,
        groups: [
          {
            group: 'PAYABLES',
            resources: [
              {
                resourceKind: 'document',
                resourceCode: 'pm.payable_charge',
                displayName: 'Payable Charges',
                actions: ['Create', 'View', 'Post'],
              },
            ],
          },
          {
            group: 'REPORTS',
            resources: [
              {
                resourceKind: 'report',
                resourceCode: 'pm.occupancy.summary',
                displayName: 'Occupancy Summary',
                actions: ['View', 'Execute'],
              },
            ],
          },
        ],
      })
      return
    }

    if (method === 'POST' && action === 'deactivate') {
      user.isActive = false
      user.keycloakEnabled = false
      await fulfillEmpty(route)
      return
    }

    if (method === 'POST' && action === 'reactivate') {
      user.isActive = true
      user.keycloakEnabled = true
      await fulfillEmpty(route)
      return
    }

    if (method === 'PUT' && action === '') {
      const payload = parseRequestJson<{
        email?: string | null
        displayName?: string | null
        roleIds?: string[]
      }>(route)
      user.email = String(payload?.email ?? user.email)
      user.displayName = String(payload?.displayName ?? user.displayName)
      user.roles = roles.filter((role) => payload?.roleIds?.includes(role.roleId))
      await fulfillJson(route, userDetails(user))
      return
    }

    await route.fallback()
  })

  return {
    getCreatedUsers: () => users.map((user) => ({ ...user, roles: user.roles.map((role) => ({ ...role })) })),
  }
}

test.describe('pm-web security access management', () => {
  test('shows backend-filtered security menu and user management create action for managers', async ({ page }) => {
    await mockSecurityMenu(page)
    await mockSecurityApis(page, true)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/security/me/access',
      '/api/security/users',
      '/api/security/roles',
    ])

    await page.goto('/admin/security/users')

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByText('Casey Morgan')).toBeVisible()
    await expect(page.getByText('PM Auditor')).toBeVisible()
    await expect(page.getByTestId('site-sidebar')).toContainText('Roles & Permissions')
    const createUserButton = page.getByTestId('site-main').getByTitle('Create')
    await expect(createUserButton).toBeVisible()
    await expect(createUserButton).toBeEnabled()

    await createUserButton.click()
    await expect(page).toHaveURL(/\/admin\/security\/users\/new$/)
  })

  test('disables create user action when backend access profile is view-only', async ({ page }) => {
    await mockSecurityMenu(page)
    await mockSecurityApis(page, false)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/security/me/access',
      '/api/security/users',
    ])

    await page.goto('/admin/security/users')

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByText('Casey Morgan')).toBeVisible()
    await expect(page.getByTestId('site-main').getByTitle('Create')).toBeDisabled()
  })

  test('redirects report-only users away from Home to their first permitted route', async ({ page }) => {
    await mockReportOnlyMenu(page)
    await mockReportOnlyAccess(page)
    await mockOccupancySummaryReportSurface(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto('/home')

    await expect(page).toHaveURL(/\/reports\/pm\.occupancy\.summary(?:\?.*)?$/)
    await expect(page.getByRole('heading', { name: 'Occupancy Summary' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Home' })).toHaveCount(0)
    await expect(page.getByTestId('site-sidebar')).toContainText('Occupancy Summary')
    await expect(page.getByTestId('site-sidebar')).not.toContainText('Home')
  })

  test('keeps direct security routes backend-enforced even when the menu hides them', async ({ page }) => {
    await mockReportOnlyMenu(page)
    await mockReportOnlyAccess(page)
    await mockForbiddenSecurityUsers(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/security/users',
    ])

    await page.goto('/admin/security/users')

    await expect(page).toHaveURL(/\/admin\/security\/users$/)
    await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()
    await expect(page.getByText('Your current access profile does not allow this operation.')).toBeVisible()
    await expect(page.getByTestId('site-sidebar')).not.toContainText('Users')
    await expect(page.getByTestId('site-sidebar')).not.toContainText('Roles & Permissions')
  })

  test('creates a user, assigns a DB role, deactivates and reactivates through the UI', async ({ page }) => {
    await mockSecurityMenu(page)
    const securityState = await mockSecurityManagementApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/security/users',
      '/api/security/roles',
    ])

    await page.goto('/admin/security/users')
    await page.getByTestId('site-main').getByTitle('Create').click()

    await expect(page).toHaveURL(/\/admin\/security\/users\/new$/)
    await inputByLabel(page, 'Email').fill('clerk@example.test')
    await inputByLabel(page, 'Display name').fill('Clerk One')
    await inputByLabel(page, 'Password').fill('Ngb#2026-Strong')
    await inputByLabel(page, 'Confirm password').fill('Ngb#2026-Strong')
    await page.getByText('PM AP Clerk', { exact: true }).click()
    await page.getByTitle('Save').click()

    await expect(page).toHaveURL(/\/admin\/security\/users\/33333333-3333-4333-8333-333333333333$/)
    await expect(page.getByRole('heading', { name: 'Clerk One' })).toBeVisible()
    expect(securityState.getCreatedUsers()[0]?.roles.map((role) => role.code)).toEqual(['pm-ap-clerk'])

    await page.getByRole('button', { name: 'Deactivate' }).click()
    await expect(page.getByText('Deactivate user?', { exact: true })).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Deactivate', exact: true }).click()
    await expect(page.getByText('Inactive', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Back' }).click()
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await page.getByRole('button', { name: 'Deleted' }).click()
    await expect(page.getByText('Clerk One')).toBeVisible()
    await expect(page.getByText('PM AP Clerk')).toBeVisible()
    await expect(page.getByText('No', { exact: true })).toBeVisible()

    await page.getByText('Clerk One').click()
    await page.getByRole('button', { name: 'Reactivate' }).click()
    await expect(page.getByText('Reactivate user?', { exact: true })).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Reactivate', exact: true }).click()
    await expect(page.getByText('Active', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Back' }).click()
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByText('Clerk One')).toBeVisible()
    await expect(page.getByText('Yes', { exact: true })).toBeVisible()
  })

  test('keeps the user editor roles and effective-access layout aligned on desktop', async ({ page }) => {
    await mockSecurityMenu(page)
    await mockSecurityManagementApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/security/users',
      '/api/security/roles',
    ])

    await page.goto('/admin/security/users')
    await page.getByTestId('site-main').getByTitle('Create').click()
    await inputByLabel(page, 'Email').fill('layout@example.test')
    await inputByLabel(page, 'Display name').fill('Layout Tester')
    await inputByLabel(page, 'Password').fill('Ngb#2026-Strong')
    await inputByLabel(page, 'Confirm password').fill('Ngb#2026-Strong')
    await page.getByText('PM AP Clerk', { exact: true }).click()
    await page.getByTitle('Save').click()

    const formPanel = page.locator('section').filter({ has: page.locator('label', { hasText: /^Email$/ }) }).first()
    const rolesPanel = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Roles' }) }).first()
    const effectiveAccessPanel = page.getByTestId('effective-access-panel')

    await expect(effectiveAccessPanel).toBeVisible()
    await expect(effectiveAccessPanel.getByTitle('Refresh')).toHaveCount(0)

    const formBox = await formPanel.boundingBox()
    const rolesBox = await rolesPanel.boundingBox()
    const effectiveBox = await effectiveAccessPanel.boundingBox()

    expect(formBox).not.toBeNull()
    expect(rolesBox).not.toBeNull()
    expect(effectiveBox).not.toBeNull()
    expect(Math.abs((rolesBox?.y ?? 0) - (formBox?.y ?? 0))).toBeLessThanOrEqual(24)
    expect(effectiveBox?.height ?? 0).toBeGreaterThanOrEqual(520)
    expect((effectiveBox?.y ?? 0) + (effectiveBox?.height ?? 0)).toBeGreaterThanOrEqual((rolesBox?.y ?? 0) + (rolesBox?.height ?? 0) - 24)
    await expectNoHorizontalPageOverflow(page)
  })
})
