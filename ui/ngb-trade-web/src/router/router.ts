import { createRouter, createWebHistory } from 'vue-router'
import {
  buildChartOfAccountsPath,
  createAuthGuard,
  ngbRouteAliasRedirectRoutes,
  useAuthStore,
} from '@ngbplatform/ui'
import { loadNgbAccountingPeriodClosingPage, loadNgbChartOfAccountsPage, loadNgbDocumentEffectsPage, loadNgbDocumentFlowPage, loadNgbDocumentPrintPage, loadNgbGeneralJournalEntryEditPage, loadNgbGeneralJournalEntryListPage, loadNgbNotificationPreferencesPage, loadNgbReportPage, loadNgbWorkCenterPage } from '@ngbplatform/ui/lazy'

import { createTradeRouteFrameworkConfig } from './framework'

const loadHomePage = () => import('../pages/HomePage.vue').then((module) => module.default)
const loadAccountingPolicySettingsPage = () => import('../pages/AccountingPolicySettingsPage.vue').then((module) => module.default)

const { catalogRoutes, documentRoutes } = createTradeRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: loadHomePage },
    { path: '/work-center', component: loadNgbWorkCenterPage, props: { vertical: 'trade' } },
    { path: '/settings/notifications', component: loadNgbNotificationPreferencesPage },

    { path: '/catalogs/trd.accounting_policy', component: loadAccountingPolicySettingsPage },
    { path: '/catalogs/trd.accounting_policy/new', redirect: '/catalogs/trd.accounting_policy' },
    { path: '/catalogs/trd.accounting_policy/:id', redirect: '/catalogs/trd.accounting_policy' },

    ...catalogRoutes,
    ...ngbRouteAliasRedirectRoutes,
    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: loadNgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: loadNgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: loadNgbDocumentPrintPage, meta: { bare: true } },

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
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))
