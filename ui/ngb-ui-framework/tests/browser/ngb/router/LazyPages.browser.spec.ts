import { describe, expect, it } from 'vitest'

import { loadNgbTrendChart } from '../../../../src/lazy'
import {
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
} from '../../../../src/ngb/router/lazyPages'

describe('lazy page entrypoints', () => {
  it('resolves every public lazy component loader', async () => {
    const loaders = [
      loadNgbTrendChart,
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
    ]

    const components = await Promise.all(loaders.map((load) => load()))

    expect(components).toHaveLength(loaders.length)
    expect(components.every(Boolean)).toBe(true)
  })
})
