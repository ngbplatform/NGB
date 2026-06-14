import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia, setActivePinia } from 'pinia'

const mocks = vi.hoisted(() => ({
  getCurrentAccess: vi.fn(),
  getRoles: vi.fn(),
  routerPush: vi.fn(),
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: mocks.getCurrentAccess,
  getRoles: mocks.getRoles,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mocks.routerPush }),
}))

import NgbRolesPage from '../../../../src/ngb/security/NgbRolesPage.vue'

beforeEach(() => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
})

test('renders roles in register layout with status filter and system text column', async () => {
  await page.viewport(1280, 900)

  mocks.getCurrentAccess.mockResolvedValue({
    userId: 'admin',
    authSubject: 'admin',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: false,
    accessVersion: 1,
    permissions: [
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'view' },
      { resourceKind: 'system', resourceCode: 'roles', actionCode: 'manage' },
    ],
  })
  mocks.getRoles.mockResolvedValue([
    {
      roleId: 'role-1',
      code: 'pm-administrator',
      name: 'PM Administrator',
      description: 'Full PM access',
      isSystem: true,
      isActive: true,
      assignedUsersCount: 1,
      createdAtUtc: '2026-06-01T00:00:00Z',
      updatedAtUtc: '2026-06-01T00:00:00Z',
    },
    {
      roleId: 'role-2',
      code: 'pm-auditor',
      name: 'PM Auditor',
      description: 'Read-only PM access',
      isSystem: false,
      isActive: false,
      assignedUsersCount: 0,
      createdAtUtc: '2026-06-01T00:00:00Z',
      updatedAtUtc: '2026-06-01T00:00:00Z',
    },
  ])

  const view = await render(NgbRolesPage)

  await expect.element(view.getByText('Roles and permissions')).toBeVisible()
  await expect.element(view.getByText('PM Administrator')).toBeVisible()
  expect(document.body.textContent).not.toContain('PM Auditor')

  await expect.element(view.getByText('Yes')).toBeVisible()
  await expect.element(view.getByText('1', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('1.00')
  expect(document.querySelector('input[type="checkbox"][aria-label^="System:"]')).toBeNull()
  expect((view.getByTitle('Create').element() as HTMLButtonElement).disabled).toBe(false)

  await view.getByRole('button', { name: 'Deleted' }).click()
  await expect.element(view.getByText('PM Auditor')).toBeVisible()
  expect(document.body.textContent).not.toContain('PM Administrator')
})
