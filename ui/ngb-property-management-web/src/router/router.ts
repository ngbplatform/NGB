import { createRouter, createWebHistory } from 'vue-router'
import { buildChartOfAccountsPath, createAuthGuard, ngbRouteAliasRedirectRoutes, NgbAccountingPeriodClosingPage, NgbChartOfAccountsPage, NgbDocumentEffectsPage, NgbDocumentFlowPage, NgbDocumentPrintPage, NgbGeneralJournalEntryEditPage, NgbGeneralJournalEntryListPage, NgbReportPage, useAuthStore } from 'ngb-ui-framework'
import { createPmRouteFrameworkConfig } from './framework'

import HomePage from '../pages/HomePage.vue'
import AccountingPolicySettingsPage from '../pages/AccountingPolicySettingsPage.vue'
import ReceivablesOpenItemsPage from '../pages/ReceivablesOpenItemsPage.vue'
import PayablesOpenItemsPage from '../pages/PayablesOpenItemsPage.vue'
import ReceivablesReconciliationPage from '../pages/ReceivablesReconciliationPage.vue'
import PayablesReconciliationPage from '../pages/PayablesReconciliationPage.vue'
import PropertiesPage from '../pages/PropertiesPage.vue'
const { catalogRoutes, documentRoutes } = createPmRouteFrameworkConfig()

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/home' },
    { path: '/home', component: HomePage },

    // Property Management: Accounting Policy is a single-record settings screen.
    { path: '/catalogs/pm.accounting_policy', component: AccountingPolicySettingsPage },
    { path: '/catalogs/pm.accounting_policy/new', redirect: '/catalogs/pm.accounting_policy' },
    { path: '/catalogs/pm.accounting_policy/:id', redirect: '/catalogs/pm.accounting_policy' },

    // Property Management: Properties (Building → Units) master-detail.
    { path: '/catalogs/pm.property', component: PropertiesPage },

    ...catalogRoutes,

    ...ngbRouteAliasRedirectRoutes,

    ...documentRoutes,
    { path: '/documents/:documentType/:id/effects', component: NgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: NgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: NgbDocumentPrintPage, meta: { bare: true } },

    { path: '/receivables/open-items', component: ReceivablesOpenItemsPage },
    { path: '/payables/open-items', component: PayablesOpenItemsPage },
    { path: '/receivables/reconciliation', component: ReceivablesReconciliationPage },
    { path: '/payables/reconciliation', component: PayablesReconciliationPage },

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
