import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  routerOptions: null as Record<string, unknown> | null,
  guards: [] as Array<(to: Record<string, unknown>) => unknown>,
  auth: { authenticated: false },
  menu: { groups: [] as unknown[], hasLoaded: false, isLoading: false, load: vi.fn().mockResolvedValue(undefined) },
  resolvePermissionAwareLanding: vi.fn(() => null as string | null),
  authGuard: vi.fn(),
}))

vi.mock('vue-router', () => ({
  createWebHistory: vi.fn(() => ({ kind: 'history' })),
  createRouter: vi.fn((options) => {
    mocks.routerOptions = options
    return { beforeEach: (guard: (to: Record<string, unknown>) => unknown) => mocks.guards.push(guard) }
  }),
}))

vi.mock('@ngbplatform/ui', () => {
  const component = {}
  return {
    buildChartOfAccountsPath: () => '/accounting/chart-of-accounts',
    createAuthGuard: (getAuth: () => unknown) => {
      getAuth()
      return mocks.authGuard
    },
    ngbRouteAliasRedirectRoutes: [{ path: '/alias' }],
    NgbAccountingPeriodClosingPage: component,
    NgbChartOfAccountsPage: component,
    NgbDocumentEffectsPage: component,
    NgbDocumentFlowPage: component,
    NgbDocumentPrintPage: component,
    NgbGeneralJournalEntryEditPage: component,
    NgbGeneralJournalEntryListPage: component,
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
  createPmRouteFrameworkConfig: () => ({
    catalogRoutes: [{ path: '/catalog-route' }],
    documentRoutes: [{ path: '/document-route' }],
  }),
}))
vi.mock('../../../src/router/permissionAwareLanding', () => ({ resolvePermissionAwareLanding: mocks.resolvePermissionAwareLanding }))

vi.mock('../../../src/pages/HomePage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/AccountingPolicySettingsPage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/ReceivablesOpenItemsPage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/PayablesOpenItemsPage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/ReceivablesReconciliationPage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/PayablesReconciliationPage.vue', () => ({ default: {} }))
vi.mock('../../../src/pages/PropertiesPage.vue', () => ({ default: {} }))

import { router } from '../../../src/router/router'

describe('property-management router', () => {
  beforeEach(() => {
    mocks.auth.authenticated = false
    mocks.menu.groups = []
    mocks.menu.hasLoaded = false
    mocks.menu.isLoading = false
    mocks.menu.load.mockClear()
    mocks.resolvePermissionAwareLanding.mockReset().mockReturnValue(null)
  })

  it('registers the complete PM and platform route surface', () => {
    expect(router).toBeTruthy()
    const routes = mocks.routerOptions!.routes as Array<{ path: string; props?: Record<string, unknown>; meta?: unknown }>
    expect(routes.map((route) => route.path)).toEqual(expect.arrayContaining([
      '/', '/home', '/work-center', '/settings/notifications',
      '/catalogs/pm.accounting_policy', '/catalogs/pm.accounting_policy/new', '/catalogs/pm.accounting_policy/:id',
      '/catalogs/pm.property', '/catalog-route', '/alias', '/document-route',
      '/receivables/open-items', '/payables/open-items',
      '/receivables/reconciliation', '/payables/reconciliation',
      '/accounting/general-journal-entries', '/accounting/general-journal-entries/new',
      '/accounting/general-journal-entries/:id', '/reports/:reportCode',
      '/admin/accounting/period-closing', '/admin/chart-of-accounts',
      '/admin/security/users', '/admin/security/users/:userId',
      '/admin/security/roles', '/admin/security/roles/:roleId',
    ]))
    expect(routes.find((route) => route.path === '/work-center')?.props).toEqual({ vertical: 'pm' })
    expect(routes.find((route) => route.path === '/admin/accounting/period-closing')?.props?.backTarget)
      .toBe('/accounting/chart-of-accounts')
    expect(mocks.guards).toHaveLength(2)
  })

  it('resolves every vertical page route lazily', async () => {
    const routes = mocks.routerOptions!.routes as Array<{ path: string; component?: () => Promise<unknown> }>
    const paths = [
      '/home',
      '/catalogs/pm.accounting_policy',
      '/catalogs/pm.property',
      '/receivables/open-items',
      '/payables/open-items',
      '/receivables/reconciliation',
      '/payables/reconciliation',
    ]

    const components = await Promise.all(paths.map((path) => routes.find((route) => route.path === path)!.component!()))

    expect(components).toEqual(paths.map(() => ({})))
  })

  it('covers bare, anonymous, redirected, loaded, and loading guard paths', async () => {
    const guard = mocks.guards[1]!
    expect(await guard({ meta: { bare: true }, path: '/print' })).toBe(true)
    expect(await guard({ meta: {}, path: '/home' })).toBe(true)
    mocks.auth.authenticated = true
    mocks.resolvePermissionAwareLanding.mockReturnValueOnce('/allowed')
    expect(await guard({ path: '/home' })).toBe('/allowed')
    expect(mocks.menu.load).toHaveBeenCalledOnce()
    mocks.menu.load.mockClear()
    mocks.menu.groups = [{}]
    mocks.menu.hasLoaded = true
    expect(await guard({ path: '/allowed' })).toBe(true)
    mocks.menu.groups = []
    mocks.menu.hasLoaded = false
    mocks.menu.isLoading = true
    expect(await guard({ path: '/allowed' })).toBe(true)
    expect(mocks.menu.load).not.toHaveBeenCalled()
  })
})
