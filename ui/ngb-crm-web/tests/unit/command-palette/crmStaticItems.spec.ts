import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', async () => {
  const [documentNavigation, reportingNavigation, staticItems] = await Promise.all([
    import('../../../../ngb-ui-framework/src/ngb/editor/documentNavigation'),
    import('../../../../ngb-ui-framework/src/ngb/reporting/navigation'),
    import('../../../../ngb-ui-framework/src/ngb/command-palette/staticItems'),
  ])

  return {
    buildDocumentFullPageUrl: documentNavigation.buildDocumentFullPageUrl,
    buildNgbHeuristicCurrentActions: staticItems.buildNgbHeuristicCurrentActions,
    buildReportPageUrl: reportingNavigation.buildReportPageUrl,
  }
})

import {
  CRM_CREATE_COMMAND_ITEMS,
  CRM_FAVORITE_ITEMS,
  CRM_SPECIAL_PAGE_ITEMS,
  resolveCRMReportPaletteIcon,
} from '../../../src/command-palette/crmStaticItems'

describe('CRM command palette static items', () => {
  it('contains CRM-native create actions only', () => {
    const routes = CRM_CREATE_COMMAND_ITEMS.map((item) => item.route)

    expect(routes).toContain('/documents/crm.lead_intake/new')
    expect(routes).toContain('/documents/crm.quote/new')
    expect(routes.every((route) => route?.includes('/documents/crm.'))).toBe(true)
  })

  it('keeps favorite and setup shortcuts on CRM routes', () => {
    const routes = [...CRM_FAVORITE_ITEMS, ...CRM_SPECIAL_PAGE_ITEMS].map((item) => item.route)

    expect(routes).toContain('/catalogs/crm.account')
    expect(routes).toContain('/catalogs/crm.opportunity_stage')
    expect(routes).toContain('/reports/crm.sales_pipeline')
    expect(routes.some((route) => String(route).includes('accounting'))).toBe(false)
  })

  it('maps CRM report icons by report purpose', () => {
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.sales_pipeline' })).toBe('bar-chart')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.quote_register' })).toBe('file-text')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.activity_summary' })).toBe('calendar-check')
  })
})
