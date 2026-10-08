import { expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({ createRouter: vi.fn(), beforeEach: vi.fn(), guard: vi.fn(), auth: vi.fn() }))
vi.mock('vue-router', () => ({
  createWebHistory: vi.fn(),
  createRouter: state.createRouter.mockReturnValue({ beforeEach: state.beforeEach }),
}))
vi.mock('@ngbplatform/ui', () => ({ createAuthGuard: state.guard, useAuthStore: state.auth }))
vi.mock('@ngbplatform/ui/lazy', () => ({
  loadNgbRolesPage: vi.fn(), loadNgbRoleEditorPage: vi.fn(), loadNgbUsersPage: vi.fn(),
  loadNgbUserEditorPage: vi.fn(), loadNgbWorkCenterPage: vi.fn(), loadNgbNotificationPreferencesPage: vi.fn(),
}))

it('guards every real platform route through the public authentication contract', async () => {
  await import('../../../packaging/templates/content/web/src/router')
  const routes = state.createRouter.mock.calls[0][0].routes
  expect(routes.map((route: { path: string }) => route.path)).toEqual([
    '/', '/admin/security/roles', '/admin/security/roles/:roleId', '/admin/security/users',
    '/admin/security/users/:userId', '/work-center', '/settings/notifications',
  ])
  expect(routes[0].redirect).toBe('/admin/security/roles')
  state.guard.mock.calls[0][0]()
  expect(state.auth).toHaveBeenCalledOnce()
  expect(state.beforeEach).toHaveBeenCalledOnce()
})
