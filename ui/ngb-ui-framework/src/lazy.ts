export const loadNgbTrendChart = () => import('./ngb/site/NgbTrendChart.vue').then((module) => module.default)

export {
  loadNgbAccountingPeriodClosingPage,
  loadNgbChartOfAccountsPage,
  loadNgbDocumentEffectsPage,
  loadNgbDocumentFlowPage,
  loadNgbDocumentPrintPage,
  loadNgbGeneralJournalEntryEditPage,
  loadNgbGeneralJournalEntryListPage,
  loadNgbMetadataCatalogEditPage,
  loadNgbMetadataCatalogListPage,
  loadNgbMetadataDocumentEditPage,
  loadNgbMetadataDocumentListPage,
  loadNgbNotificationPreferencesPage,
  loadNgbReportPage,
  loadNgbRoleEditorPage,
  loadNgbRolesPage,
  loadNgbUserEditorPage,
  loadNgbUsersPage,
  loadNgbWorkCenterPage,
} from './ngb/router/lazyPages'
