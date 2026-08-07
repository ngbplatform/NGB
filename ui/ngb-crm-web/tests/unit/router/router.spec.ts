import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  routerOptions: null as Record<string, unknown> | null,
  guards: [] as Array<(to: Record<string, unknown>) => unknown>,
  auth: { authenticated: false },
  menu: { groups: [] as unknown[], isLoading: false, load: vi.fn().mockResolvedValue(undefined) },
  resolvePermissionAwareLanding: vi.fn(() => null as string | null),
  authGuard: vi.fn(),
}))

vi.mock('vue-router', () => ({
  createWebHistory: vi.fn(() => ({ kind: 'history' })),
  createRouter: vi.fn((options) => {
    mocks.routerOptions = options
    return {
      beforeEach: (guard: (to: Record<string, unknown>) => unknown) => mocks.guards.push(guard),
    }
  }),
}))

vi.mock('@ngbplatform/ui', () => {
  const component = {}
  return {
    createAuthGuard: (getAuth: () => unknown) => {
      getAuth()
      return mocks.authGuard
    },
    ngbRouteAliasRedirectRoutes: [{ path: '/alias' }],
    NgbDocumentEffectsPage: component,
    NgbDocumentFlowPage: component,
    NgbDocumentPrintPage: component,
    NgbNotificationPreferencesPage: component,
    NgbReportPage: component,
    NgbRoleEditorPage: component,
    NgbRolesPage: component,
    NgbUserEditorPage: component,
    NgbUsersPage: component,
    NgbWorkCenterPage: component,
    useAuthStore: () => mocks.auth,
    useMainMenuStore: () => mocks.menu,
  }
})

vi.mock('../../../src/router/framework', () => ({
  createCRMRouteFrameworkConfig: () => ({
    catalogRoutes: [{ path: '/catalog-route' }],
    documentRoutes: [{ path: '/document-route' }],
  }),
}))

vi.mock('../../../src/router/permissionAwareLanding', () => ({
  resolvePermissionAwareLanding: mocks.resolvePermissionAwareLanding,
}))

vi.mock('../../../src/pages/HomePage.vue', () => ({ default: {} }))

import { router } from '../../../src/router/router'

describe('CRM router', () => {
  beforeEach(() => {
    mocks.auth.authenticated = false
    mocks.menu.groups = []
    mocks.menu.isLoading = false
    mocks.menu.load.mockClear()
    mocks.resolvePermissionAwareLanding.mockReset().mockReturnValue(null)
  })

  it('registers the complete platform and vertical route surface', () => {
    expect(router).toBeTruthy()
    const routes = mocks.routerOptions!.routes as Array<{ path: string; props?: unknown; meta?: unknown }>
    expect(routes.map((route) => route.path)).toEqual(expect.arrayContaining([
      '/', '/home', '/work-center', '/settings/notifications',
      '/catalog-route', '/alias', '/document-route',
      '/documents/:documentType/:id/effects',
      '/documents/:documentType/:id/flow',
      '/documents/:documentType/:id/print',
      '/reports/:reportCode',
      '/admin/security/users', '/admin/security/users/:userId',
      '/admin/security/roles', '/admin/security/roles/:roleId',
    ]))
    expect(routes.find((route) => route.path === '/work-center')?.props).toEqual({ vertical: 'crm' })
    expect(routes.find((route) => route.path.endsWith('/print'))?.meta).toEqual({ bare: true })
    expect(mocks.guards).toHaveLength(2)
    expect(mocks.guards[0]).toBe(mocks.authGuard)
  })

  it('lets bare and unauthenticated navigation through', async () => {
    expect(await mocks.guards[1]!({ meta: { bare: true }, path: '/print' })).toBe(true)
    expect(await mocks.guards[1]!({ meta: {}, path: '/home' })).toBe(true)
    expect(mocks.menu.load).not.toHaveBeenCalled()
  })

  it('loads an empty idle menu and applies a permission-aware redirect', async () => {
    mocks.auth.authenticated = true
    mocks.resolvePermissionAwareLanding.mockReturnValueOnce('/allowed')
    expect(await mocks.guards[1]!({ meta: {}, path: '/home' })).toBe('/allowed')
    expect(mocks.menu.load).toHaveBeenCalledOnce()
  })

  it('returns true for an allowed route and skips menu loading when loaded or loading', async () => {
    mocks.auth.authenticated = true
    mocks.menu.groups = [{}]
    expect(await mocks.guards[1]!({ path: '/allowed' })).toBe(true)
    mocks.menu.groups = []
    mocks.menu.isLoading = true
    expect(await mocks.guards[1]!({ path: '/allowed' })).toBe(true)
    expect(mocks.menu.load).not.toHaveBeenCalled()
  })
})
