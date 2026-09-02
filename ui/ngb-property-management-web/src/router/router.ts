import { createRouter, createWebHistory } from 'vue-router'
import { buildChartOfAccountsPath, createAuthGuard, ngbRouteAliasRedirectRoutes, useAuthStore, useMainMenuStore } from '@ngbplatform/ui'
import { loadNgbAccountingPeriodClosingPage, loadNgbChartOfAccountsPage, loadNgbDocumentEffectsPage, loadNgbDocumentFlowPage, loadNgbDocumentPrintPage, loadNgbGeneralJournalEntryEditPage, loadNgbGeneralJournalEntryListPage, loadNgbNotificationPreferencesPage, loadNgbReportPage, loadNgbRoleEditorPage, loadNgbRolesPage, loadNgbUserEditorPage, loadNgbUsersPage, loadNgbWorkCenterPage } from '@ngbplatform/ui/lazy'
import { createPmRouteFrameworkConfig } from './framework'
import { resolvePermissionAwareLanding } from './permissionAwareLanding'

const loadHomePage = () => import('../pages/HomePage.vue').then((module) => module.default)
const loadAccountingPolicySettingsPage = () => import('../pages/AccountingPolicySettingsPage.vue').then((module) => module.default)
const loadReceivablesOpenItemsPage = () => import('../pages/ReceivablesOpenItemsPage.vue').then((module) => module.default)
const loadPayablesOpenItemsPage = () => import('../pages/PayablesOpenItemsPage.vue').then((module) => module.default)
const loadReceivablesReconciliationPage = () => import('../pages/ReceivablesReconciliationPage.vue').then((module) => module.default)
const loadPayablesReconciliationPage = () => import('../pages/PayablesReconciliationPage.vue').then((module) => module.default)
const loadPropertiesPage = () => import('../pages/PropertiesPage.vue').then((module) => module.default)
const { catalogRoutes, documentRoutes } = createPmRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: loadHomePage },
    { path: '/work-center', component: loadNgbWorkCenterPage, props: { vertical: 'pm' } },
    { path: '/settings/notifications', component: loadNgbNotificationPreferencesPage },

    // Property Management: Accounting Policy is a single-record settings screen.
    { path: '/catalogs/pm.accounting_policy', component: loadAccountingPolicySettingsPage },
    { path: '/catalogs/pm.accounting_policy/new', redirect: '/catalogs/pm.accounting_policy' },
    { path: '/catalogs/pm.accounting_policy/:id', redirect: '/catalogs/pm.accounting_policy' },

    // Property Management: Properties (Building → Units) master-detail.
    { path: '/catalogs/pm.property', component: loadPropertiesPage },

    ...catalogRoutes,

    ...ngbRouteAliasRedirectRoutes,

    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: loadNgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: loadNgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: loadNgbDocumentPrintPage, meta: { bare: true } },

    { path: '/receivables/open-items', component: loadReceivablesOpenItemsPage },
    { path: '/payables/open-items', component: loadPayablesOpenItemsPage },
    { path: '/receivables/reconciliation', component: loadReceivablesReconciliationPage },
    { path: '/payables/reconciliation', component: loadPayablesReconciliationPage },

    { path: '/accounting/general-journal-entries', component: loadNgbGeneralJournalEntryListPage },
    { path: '/accounting/general-journal-entries/new', component: loadNgbGeneralJournalEntryEditPage },
    { path: '/accounting/general-journal-entries/:id', component: loadNgbGeneralJournalEntryEditPage },


    { path: '/reports/:reportCode', component: loadNgbReportPage },
    {
      path: '/admin/accounting/period-closing',
      component: loadNgbAccountingPeriodClosingPage,
      props: {
        backTarget: buildChartOfAccountsPath(),
      },
    },
    { path: '/admin/chart-of-accounts', component: loadNgbChartOfAccountsPage },
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
