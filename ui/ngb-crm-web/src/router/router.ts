import { createRouter, createWebHistory } from 'vue-router'
import {
  createAuthGuard,
  ngbRouteAliasRedirectRoutes,
  useAuthStore,
  useMainMenuStore,
} from '@ngbplatform/ui'
import { loadNgbDocumentEffectsPage, loadNgbDocumentFlowPage, loadNgbDocumentPrintPage, loadNgbNotificationPreferencesPage, loadNgbReportPage, loadNgbRoleEditorPage, loadNgbRolesPage, loadNgbUserEditorPage, loadNgbUsersPage, loadNgbWorkCenterPage } from '@ngbplatform/ui/lazy'

import { createCRMRouteFrameworkConfig } from './framework'
import { resolvePermissionAwareLanding } from './permissionAwareLanding'

const loadHomePage = () => import('../pages/HomePage.vue').then((module) => module.default)

const { catalogRoutes, documentRoutes } = createCRMRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: loadHomePage },
    { path: '/work-center', component: loadNgbWorkCenterPage, props: { vertical: 'crm' } },
    { path: '/settings/notifications', component: loadNgbNotificationPreferencesPage },

    ...catalogRoutes,
    ...ngbRouteAliasRedirectRoutes,
    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: loadNgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: loadNgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: loadNgbDocumentPrintPage, meta: { bare: true } },

    { path: '/reports/:reportCode', component: loadNgbReportPage },
    { path: '/admin/security/users', component: loadNgbUsersPage },
    { path: '/admin/security/users/:userId', component: loadNgbUserEditorPage },
    { path: '/admin/security/roles', component: loadNgbRolesPage },
    { path: '/admin/security/roles/:roleId', component: loadNgbRoleEditorPage },
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))

router.beforeEach(async (to) => {
  if (to.meta?.bare === true) return true

  const auth = useAuthStore()
  if (!auth.authenticated) return true

  const menu = useMainMenuStore()
  if (!menu.hasLoaded && !menu.isLoading) {
    await menu.load()
  }

  return resolvePermissionAwareLanding(menu.groups, to.path) ?? true
})
