import { beforeEach, describe, expect, it, vi } from 'vitest'

import { configureNgbReporting, resolveReportCellActionUrl, resolveReportLookupTarget } from '../../../../src/ngb/reporting/config'
import { decodeReportRouteContextParam, decodeReportSourceTrailParam, type ReportRouteContext } from '../../../../src/ngb/reporting/navigation'
import { decodeBackTarget } from '../../../../src/ngb/router/backNavigation'

const documentId = '11111111-1111-1111-1111-111111111111'
const catalogId = '22222222-2222-2222-2222-222222222222'
const accountId = '33333333-3333-3333-3333-333333333333'

const currentReportContext: ReportRouteContext = {
  reportCode: 'pm.occupancy.summary',
  reportName: 'Occupancy Summary',
  request: {
    parameters: {
      as_of_utc: '2026-04-08',
    },
    filters: {
      status: {
        value: 'open',
      },
    },
    layout: null,
    offset: 0,
    limit: 500,
    cursor: null,
    variantCode: 'portfolio-view',
  },
}

describe('reporting config helpers', () => {
  beforeEach(() => {
    configureNgbReporting({
      useLookupStore: () => ({
        searchCatalog: async () => [],
        searchCoa: async () => [],
        searchDocuments: async () => [],
        ensureCatalogLabels: async () => undefined,
        ensureCoaLabels: async () => undefined,
        ensureAnyDocumentLabels: async () => undefined,
        labelForCatalog: (_, id) => String(id),
        labelForCoa: (id) => String(id),
        labelForAnyDocument: (_documentTypes, id) => String(id),
      }),
    })
  })

  it('builds document, catalog, and account drilldown urls with an encoded back target', () => {
    const backTarget = '/reports/source'

    const documentUrl = new URL(`https://example.test${resolveReportCellActionUrl({
      kind: 'open_document',
      documentType: 'pm.invoice',
      documentId,
    }, { backTarget })}`)

    expect(documentUrl.pathname).toBe(`/documents/pm.invoice/${documentId}`)
    expect(decodeBackTarget(documentUrl.searchParams.get('back'))).toBe(backTarget)

    const catalogUrl = new URL(`https://example.test${resolveReportCellActionUrl({
      kind: 'open_catalog',
      catalogType: 'pm.property',
      catalogId,
    }, { backTarget })}`)

    expect(catalogUrl.pathname).toBe(`/catalogs/pm.property/${catalogId}`)
    expect(decodeBackTarget(catalogUrl.searchParams.get('back'))).toBe(backTarget)

    const accountUrl = new URL(`https://example.test${resolveReportCellActionUrl({
      kind: 'open_account',
      accountId,
    }, { backTarget })}`)

    expect(accountUrl.pathname).toBe('/admin/chart-of-accounts')
    expect(accountUrl.searchParams.get('panel')).toBe('edit')
    expect(accountUrl.searchParams.get('id')).toBe(accountId)
    expect(decodeBackTarget(accountUrl.searchParams.get('back'))).toBe(backTarget)
  })

  it('builds open-report drilldown urls with context, source trail, and back target', () => {
    const url = new URL(`https://example.test${resolveReportCellActionUrl({
      kind: 'open_report',
      report: {
        reportCode: 'pm.occupancy.detail',
        parameters: {
          as_of_utc: '2026-03-31',
        },
        filters: {
          property: {
            value: catalogId,
          },
        },
      },
    }, {
      currentReportContext,
      sourceTrail: {
        items: [
          {
            reportCode: 'pm.portfolio.home',
            request: {
              parameters: null,
              filters: null,
              layout: null,
              offset: 0,
              limit: 500,
              cursor: null,
            },
          },
        ],
      },
      backTarget: '/reports/home',
    })}`)

    expect(url.pathname).toBe('/reports/pm.occupancy.detail')
    expect(decodeBackTarget(url.searchParams.get('back'))).toBe('/reports/home')

    expect(decodeReportRouteContextParam(url.searchParams.get('ctx'))).toEqual({
      reportCode: 'pm.occupancy.detail',
      reportName: null,
      request: {
        parameters: {
          as_of_utc: '2026-03-31',
        },
        filters: {
          property: {
            value: catalogId,
            includeDescendants: false,
          },
        },
        layout: null,
        offset: 0,
        limit: 500,
        cursor: null,
        variantCode: null,
      },
    })

    expect(decodeReportSourceTrailParam(url.searchParams.get('src'))).toEqual({
      items: [
        {
          reportCode: 'pm.portfolio.home',
          reportName: null,
          request: {
            parameters: null,
            filters: null,
            layout: null,
            offset: 0,
            limit: 500,
            cursor: null,
            variantCode: null,
          },
        },
        {
          reportCode: 'pm.occupancy.summary',
          reportName: 'Occupancy Summary',
          request: {
            parameters: {
              as_of_utc: '2026-04-08',
            },
            filters: {
              status: {
                value: 'open',
                includeDescendants: false,
              },
            },
            layout: null,
            offset: 0,
            limit: 500,
            cursor: null,
            variantCode: 'portfolio-view',
          },
        },
      ],
    })
  })

  it('returns null for invalid drilldown ids and lets configured overrides win', async () => {
    expect(resolveReportCellActionUrl({
      kind: 'open_document',
      documentType: 'pm.invoice',
      documentId: 'not-a-guid',
    })).toBeNull()

    const resolveLookupTarget = vi.fn(async () => '/catalogs/pm.property/custom')
    const resolveCellActionUrl = vi.fn(() => '/custom-action')

    configureNgbReporting({
      useLookupStore: () => ({
        searchCatalog: async () => [],
        searchCoa: async () => [],
        searchDocuments: async () => [],
        ensureCatalogLabels: async () => undefined,
        ensureCoaLabels: async () => undefined,
        ensureAnyDocumentLabels: async () => undefined,
        labelForCatalog: (_, id) => String(id),
        labelForCoa: (id) => String(id),
        labelForAnyDocument: (_documentTypes, id) => String(id),
      }),
      resolveLookupTarget,
      resolveCellActionUrl,
    })

    expect(resolveReportCellActionUrl({
      kind: 'open_document',
      documentType: 'pm.invoice',
      documentId,
    })).toBe('/custom-action')

    expect(await resolveReportLookupTarget({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      value: {
        id: catalogId,
        label: 'Riverfront Tower',
      },
      routeFullPath: '/reports/pm.occupancy.summary',
    })).toBe('/catalogs/pm.property/custom')
  })
})
