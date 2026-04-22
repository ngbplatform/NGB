import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

import { configureNgbReporting } from '../../../../src/ngb/reporting/config'
import { decodeReportRouteContextParam, decodeReportSourceTrailParam, type ReportRouteContext } from '../../../../src/ngb/reporting/navigation'
import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import { ReportRowKind, type ReportSheetDto } from '../../../../src/ngb/reporting/types'
import { decodeBackTarget } from '../../../../src/ngb/router/backNavigation'

const documentId = '11111111-1111-1111-1111-111111111111'
const propertyId = '22222222-2222-2222-2222-222222222222'

const AppRoot = defineComponent({
  setup() {
    return () => h(RouterView)
  },
})

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

function bodyActionSheet(): ReportSheetDto {
  return {
    columns: [
      { code: 'document', title: 'Document', dataType: 'string' },
      { code: 'property', title: 'Property', dataType: 'string' },
    ],
    rows: [
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          {
            display: 'Invoice INV-001',
            value: 'Invoice INV-001',
            valueType: 'string',
            action: {
              kind: 'open_document',
              documentType: 'pm.invoice',
              documentId,
            },
          },
          {
            display: 'Riverfront Tower',
            value: 'Riverfront Tower',
            valueType: 'string',
          },
        ],
      },
    ],
  }
}

function headerActionSheet(): ReportSheetDto {
  return {
    columns: [
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'units', title: 'Units', dataType: 'number' },
    ],
    headerRows: [
      {
        rowKind: ReportRowKind.Header,
        cells: [
          {
            display: 'Property Drilldown',
            value: 'Property Drilldown',
            rowSpan: 2,
            action: {
              kind: 'open_report',
              report: {
                reportCode: 'pm.property.detail',
                parameters: {
                  as_of_utc: '2026-03-31',
                },
                filters: {
                  property: {
                    value: propertyId,
                  },
                },
              },
            },
          },
          {
            display: 'Snapshot',
            value: 'Snapshot',
            colSpan: 1,
          },
        ],
      },
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Units', value: 'Units' },
        ],
      },
    ],
    rows: [
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          {
            display: 'Riverfront Tower',
            value: 'Riverfront Tower',
            valueType: 'string',
          },
          {
            display: '24',
            value: 24,
            valueType: 'decimal',
          },
        ],
      },
    ],
  }
}

async function renderSheet(
  sheet: ReportSheetDto,
  options?: {
    currentReportContext?: ReportRouteContext | null
    sourceTrail?: { items: ReportRouteContext[] } | null
    backTarget?: string | null
  },
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/sheet',
        component: defineComponent({
          setup() {
            return () => h('div', {
              style: 'width: 960px; max-width: 960px; height: 720px; display: flex; min-width: 0; min-height: 0; overflow: hidden;',
            }, [
              h(NgbReportSheet, {
                sheet,
                currentReportContext: options?.currentReportContext ?? null,
                sourceTrail: options?.sourceTrail ?? null,
                backTarget: options?.backTarget ?? null,
                rowNoun: 'property',
              }),
            ])
          },
        }),
      },
      {
        path: '/documents/:documentType/:id',
        component: {
          template: '<div data-testid="document-target-page">Document target</div>',
        },
      },
      {
        path: '/reports/:reportCode',
        component: {
          template: '<div data-testid="report-target-page">Report target</div>',
        },
      },
    ],
  })

  await router.push('/sheet')
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [router],
    },
  })

  return { router, view }
}

configureNgbReporting({
  useLookupStore: () => ({
    searchCatalog: async () => [],
    searchCoa: async () => [],
    searchDocuments: async () => [],
    ensureCatalogLabels: async () => undefined,
    ensureCoaLabels: async () => undefined,
    ensureAnyDocumentLabels: async () => undefined,
    labelForCatalog: (_catalogType, id) => String(id),
    labelForCoa: (id) => String(id),
    labelForAnyDocument: (_documentTypes, id) => String(id),
  }),
})

test('navigates from a body cell drilldown to the document route with back target context', async () => {
  await page.viewport(960, 800)

  const { router, view } = await renderSheet(bodyActionSheet(), {
    backTarget: '/reports/source',
  })

  await expect.element(view.getByText('Invoice INV-001', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Invoice INV-001' }).click()

  expect(router.currentRoute.value.params.documentType).toBe('pm.invoice')
  expect(router.currentRoute.value.params.id).toBe(documentId)
  expect(decodeBackTarget(router.currentRoute.value.query.back)).toBe('/reports/source')
  await expect.element(view.getByTestId('document-target-page')).toBeVisible()
})

test('navigates from a header drilldown into another report with appended source trail', async () => {
  await page.viewport(960, 800)

  const { router, view } = await renderSheet(headerActionSheet(), {
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
            variantCode: null,
          },
        },
      ],
    },
    backTarget: '/reports/source-home',
  })

  await expect.element(view.getByRole('button', { name: 'Property Drilldown' })).toBeVisible()
  await view.getByRole('button', { name: 'Property Drilldown' }).click()

  expect(router.currentRoute.value.params.reportCode).toBe('pm.property.detail')
  expect(decodeBackTarget(router.currentRoute.value.query.back)).toBe('/reports/source-home')
  expect(decodeReportRouteContextParam(router.currentRoute.value.query.ctx)).toEqual({
    reportCode: 'pm.property.detail',
    reportName: null,
    request: {
      parameters: {
        as_of_utc: '2026-03-31',
      },
      filters: {
        property: {
          value: propertyId,
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
  expect(decodeReportSourceTrailParam(router.currentRoute.value.query.src)).toEqual({
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
        ...currentReportContext,
        request: {
          ...currentReportContext.request,
          filters: {
            status: {
              value: 'open',
              includeDescendants: false,
            },
          },
        },
      },
    ],
  })
  await expect.element(view.getByTestId('report-target-page')).toBeVisible()
})
