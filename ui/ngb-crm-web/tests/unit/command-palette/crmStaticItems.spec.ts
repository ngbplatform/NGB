import { describe, expect, it, vi } from 'vitest'

vi.mock('@ngbplatform/ui', () => ({
  buildDocumentFullPageUrl: (documentType: string) => `/documents/${documentType}/new`,
  buildNgbHeuristicCurrentActions: (fullRoute: string) => [{ key: `heuristic:${fullRoute}` }],
  buildReportPageUrl: (reportCode: string) => `/reports/${reportCode}`,
}))

import {
  CRM_CREATE_COMMAND_ITEMS,
  CRM_FAVORITE_ITEMS,
  CRM_SPECIAL_PAGE_ITEMS,
  buildCRMHeuristicCurrentActions,
  resolveCRMReportPaletteIcon,
} from '../../../src/command-palette/crmStaticItems'

describe('CRM command palette static items', () => {
  it('delegates heuristic discovery for CRM routes', () => {
    expect(buildCRMHeuristicCurrentActions('/documents/crm.quote')).toEqual([
      { key: 'heuristic:/documents/crm.quote' },
    ])
  })

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
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.lead_conversion_funnel' })).toBe('filter')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.quote_register' })).toBe('file-text')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.activity_summary' })).toBe('calendar-check')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'crm.opportunity_history' })).toBe('history')
    expect(resolveCRMReportPaletteIcon({ reportCode: 'unknown' })).toBe('bar-chart')
    expect(resolveCRMReportPaletteIcon({ reportCode: null })).toBe('bar-chart')
  })
})
