import { expect, test, type Page, type Route } from '@playwright/test'

import { rejectUnhandledApiRequests } from '../support/mockApi'

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
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

async function mockSecurityApis(page: Page, canManageUsers: boolean): Promise<void> {
  await page.route('**/api/security/me/access', async (route) => {
    await fulfillJson(route, {
      userId: 'admin-user',
      authSubject: 'admin-user',
      isAuthenticated: true,
      isActive: true,
      isBootstrapAdmin: false,
      accessVersion: 1,
      permissions: [
        { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
        ...(canManageUsers ? [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'manage' }] : []),
      ],
    })
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
})
