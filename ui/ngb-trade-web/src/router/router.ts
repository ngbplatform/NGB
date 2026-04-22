import { createRouter, createWebHistory } from 'vue-router'
import {
  buildChartOfAccountsPath,
  createAuthGuard,
  ngbRouteAliasRedirectRoutes,
  NgbAccountingPeriodClosingPage,
  NgbChartOfAccountsPage,
  NgbDocumentEffectsPage,
  NgbDocumentFlowPage,
  NgbDocumentPrintPage,
  NgbGeneralJournalEntryEditPage,
  NgbGeneralJournalEntryListPage,
  NgbReportPage,
  useAuthStore,
} from 'ngb-ui-framework'

import { createTradeRouteFrameworkConfig } from './framework'

import HomePage from '../pages/HomePage.vue'
import AccountingPolicySettingsPage from '../pages/AccountingPolicySettingsPage.vue'

const { catalogRoutes, documentRoutes } = createTradeRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: HomePage },

    { path: '/catalogs/trd.accounting_policy', component: AccountingPolicySettingsPage },
    { path: '/catalogs/trd.accounting_policy/new', redirect: '/catalogs/trd.accounting_policy' },
    { path: '/catalogs/trd.accounting_policy/:id', redirect: '/catalogs/trd.accounting_policy' },

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
