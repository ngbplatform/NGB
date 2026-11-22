import { createRouter, createWebHistory } from 'vue-router'
import { createAuthGuard, useAuthStore } from '@ngbplatform/ui'
import {
  loadNgbAccountingPeriodClosingPage,
  loadNgbDocumentEffectsPage,
  loadNgbDocumentFlowPage,
  loadNgbDocumentPrintPage,
  loadNgbGeneralJournalEntryEditPage,
  loadNgbGeneralJournalEntryListPage,
  loadNgbMetadataCatalogEditPage,
  loadNgbMetadataDocumentEditPage,
  loadNgbNotificationPreferencesPage,
  loadNgbReportPage,
  loadNgbWorkCenterPage,
} from '@ngbplatform/ui/lazy'
import EntityEditor from './EntityEditor.vue'
import InspectionSourcePicker from './InspectionSourcePicker.vue'
import CatalogPage from './pages/CatalogPage.vue'
import InspectionPage from './pages/InspectionPage.vue'
import NotFoundPage from './pages/NotFoundPage.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/inspection-sources/:equipmentId', component: InspectionSourcePicker, props: true },
    { path: '/', redirect: '/catalogs/example.equipment' },
    { path: '/catalogs/:catalogType', component: CatalogPage },
    { path: '/catalogs/:catalogType/:id', component: loadNgbMetadataCatalogEditPage, props: { editorComponent: EntityEditor } },
    { path: '/documents/:documentType', component: InspectionPage },
    { path: '/documents/:documentType/:id', component: loadNgbMetadataDocumentEditPage, props: { editorComponent: EntityEditor } },
    { path: '/documents/:documentType/:id/effects', component: loadNgbDocumentEffectsPage },
    { path: '/documents/:documentType/:id/flow', component: loadNgbDocumentFlowPage },
    { path: '/documents/:documentType/:id/print', component: loadNgbDocumentPrintPage },
    { path: '/reports/:reportCode', component: loadNgbReportPage },
    { path: '/accounting/general-journal-entries', component: loadNgbGeneralJournalEntryListPage },
    { path: '/accounting/general-journal-entries/new', component: loadNgbGeneralJournalEntryEditPage },
    { path: '/accounting/general-journal-entries/:id', component: loadNgbGeneralJournalEntryEditPage },
    { path: '/admin/accounting/period-closing', component: loadNgbAccountingPeriodClosingPage },
    { path: '/work-center', component: loadNgbWorkCenterPage },
    { path: '/settings/notifications', component: loadNgbNotificationPreferencesPage },
    { path: '/:pathMatch(.*)*', component: NotFoundPage },
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))
