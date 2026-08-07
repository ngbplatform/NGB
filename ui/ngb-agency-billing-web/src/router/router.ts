import { createRouter, createWebHistory } from 'vue-router'
import {
  buildChartOfAccountsPath,
  createAuthGuard,
  NgbAccountingPeriodClosingPage,
  NgbChartOfAccountsPage,
  NgbDocumentEffectsPage,
  NgbDocumentFlowPage,
  NgbDocumentPrintPage,
  NgbGeneralJournalEntryEditPage,
  NgbGeneralJournalEntryListPage,
  NgbNotificationPreferencesPage,
  ngbRouteAliasRedirectRoutes,
  NgbReportPage,
  NgbWorkCenterPage,
  useAuthStore,
} from '@ngbplatform/ui'

import { createAgencyBillingRouteFrameworkConfig } from './framework'

import HomePage from '../pages/HomePage.vue'
import AccountingPolicySettingsPage from '../pages/AccountingPolicySettingsPage.vue'

const { catalogRoutes, documentRoutes } = createAgencyBillingRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: HomePage },
    { path: '/work-center', component: NgbWorkCenterPage, props: { vertical: 'ab' } },
    { path: '/settings/notifications', component: NgbNotificationPreferencesPage },

    { path: '/catalogs/ab.accounting_policy', component: AccountingPolicySettingsPage },
    { path: '/catalogs/ab.accounting_policy/new', redirect: '/catalogs/ab.accounting_policy' },
    { path: '/catalogs/ab.accounting_policy/:id', redirect: '/catalogs/ab.accounting_policy' },

    ...catalogRoutes,
    ...ngbRouteAliasRedirectRoutes,
    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: NgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: NgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: NgbDocumentPrintPage, meta: { bare: true } },

    { path: '/accounting/general-journal-entries', component: NgbGeneralJournalEntryListPage },
    { path: '/accounting/general-journal-entries/new', component: NgbGeneralJournalEntryEditPage },
    { path: '/accounting/general-journal-entries/:id', component: NgbGeneralJournalEntryEditPage },

    { path: '/reports/:reportCode', component: NgbReportPage },
    {
      path: '/admin/accounting/period-closing',
      component: NgbAccountingPeriodClosingPage,
      props: {
        backTarget: buildChartOfAccountsPath(),
      },
    },
    { path: '/admin/chart-of-accounts', component: NgbChartOfAccountsPage },
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))
