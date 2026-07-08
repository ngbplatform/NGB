import { createRouter, createWebHistory } from 'vue-router'
import {
  createAuthGuard,
  ngbRouteAliasRedirectRoutes,
  NgbDocumentEffectsPage,
  NgbDocumentFlowPage,
  NgbDocumentPrintPage,
  NgbReportPage,
  NgbRoleEditorPage,
  NgbRolesPage,
  NgbUserEditorPage,
  NgbUsersPage,
  useAuthStore,
} from '@ngbplatform/ui'

import { createCRMRouteFrameworkConfig } from './framework'

import HomePage from '../pages/HomePage.vue'

const { catalogRoutes, documentRoutes } = createCRMRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: HomePage },

    ...catalogRoutes,
    ...ngbRouteAliasRedirectRoutes,
    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: NgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: NgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: NgbDocumentPrintPage, meta: { bare: true } },

    { path: '/reports/:reportCode', component: NgbReportPage },
    { path: '/admin/security/users', component: NgbUsersPage },
    { path: '/admin/security/users/:userId', component: NgbUserEditorPage },
    { path: '/admin/security/roles', component: NgbRolesPage },
    { path: '/admin/security/roles/:roleId', component: NgbRoleEditorPage },
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))
