import { beforeEach, describe, expect, it, vi } from 'vitest'

const executeReportMock = vi.hoisted(() => vi.fn())

vi.mock('@ngbplatform/ui', () => ({
  ReportRowKind: {
    Group: 2,
    Detail: 3,
  },
  buildDocumentFullPageUrl: (typeCode: string, id: string) => `/documents/${typeCode}/${id}`,
  buildReportPageUrl: (reportCode: string) => `/reports/${reportCode}`,
  dashboardReportCellByCode: (row: any, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    return index == null ? null : row.cells[index] ?? null
  },
  dashboardReportCellDisplay: (row: any, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    return index == null ? '' : String(row.cells[index]?.display ?? '').trim()
  },
  dashboardReportCellNumber: (row: any, columns: Map<string, number>, code: string) => {
    const index = columns.get(code)
    if (index == null) return 0
    return Number(row.cells[index]?.value ?? row.cells[index]?.display ?? 0)
  },
  dashboardReportColumnIndexMap: (response: any) =>
    new Map((response.sheet.columns ?? []).map((column: any, index: number) => [String(column.code ?? ''), index])),
  executeReport: executeReportMock,
  formatDashboardMonthLabel: (monthKey: string) => monthKey,
  isDashboardReportRowKind: (row: any, kind: number) => {
    const normalized = String(row.rowKind).toLowerCase()
    if (kind === 2) return normalized === 'group'
    if (kind === 3) return normalized === 'detail'
    return false
  },
  resolveReportCellActionUrl: (action: any) =>
    action?.kind === 'open_document' ? `/documents/${action.documentType}/${action.documentId}` : null,
  startOfDashboardUtcMonth: (date: Date) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)),
  toDashboardUtcDateOnly: (date: Date) => date.toISOString().slice(0, 10),
  toDashboardUtcMonthKey: (date: Date) =>
    `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`,
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

type CellValue = string | number

function report(columns: string[], rows: CellValue[][], actions: Record<string, any> = {}, rowKind = 'Detail') {
  return {
    sheet: {
      columns: columns.map((code) => ({ code })),
      rows: rows.map((values) => ({
        rowKind,
        cells: values.map((value, index) => {
          const code = columns[index]
          return { value, display: String(value), action: actions[code] ?? null }
        }),
      })),
    },
  }
}

describe('CRM home dashboard data', () => {
  beforeEach(() => {
    executeReportMock.mockReset()
  })

  it('reads composable report measure columns with aggregation suffixes', async () => {
    executeReportMock.mockImplementation((reportCode: string) => {
      switch (reportCode) {
        case 'crm.sales_pipeline':
          return Promise.resolve(report(
            ['opportunity_display', 'customer_display', 'stage_display', 'amount__sum', 'weighted_amount__sum'],
            [
              ['Enterprise CRM rollout', 'Acme Distribution', 'Proposal', 1000, 400],
              ['', '', '', 200, 600],
              ['Filtered opportunity', 'Filtered account', 'Prospect', 0, 0],
            ],
            {
              opportunity_display: {
                kind: 'open_document',
                documentType: 'crm.lead_conversion',
                documentId: 'opp-1',
              },
            },
          ))
        case 'crm.lead_conversion_funnel':
          return Promise.resolve(report(
            ['funnel_step', 'lead_count__sum'],
            [
              ['01 Intake', 2],
              ['02 Qualified', 1],
              ['03 Converted', 1],
            ],
          ))
        case 'crm.activity_summary':
          return Promise.resolve(report(['activity_count__sum'], [[4]]))
        case 'crm.quote_register':
          return Promise.resolve(report(['amount__sum', 'quote_count__sum'], [[250, 3]]))
        default:
          throw new Error(`Unexpected report ${reportCode}`)
      }
    })

    const data = await loadHomeDashboard('2026-07-05')

    expect(data.pipelineAmount).toBe(1200)
    expect(data.weightedPipelineAmount).toBe(1000)
    expect(data.leadCount).toBe(2)
    expect(data.qualifiedLeadCount).toBe(1)
    expect(data.convertedLeadCount).toBe(1)
    expect(data.activityCount).toBe(4)
    expect(data.quoteAmount).toBe(250)
    expect(data.quoteCount).toBe(3)
    expect(data.openOpportunities).toEqual([
      {
        opportunity: 'Opportunity',
        account: 'Account',
        stage: 'Stage',
        amount: 200,
        weightedAmount: 600,
        route: '/documents/crm.lead_conversion/opp-1',
      },
      {
        opportunity: 'Enterprise CRM rollout',
        account: 'Acme Distribution',
        stage: 'Proposal',
        amount: 1000,
        weightedAmount: 400,
        route: '/documents/crm.lead_conversion/opp-1',
      },
    ])

    const funnelRequest = executeReportMock.mock.calls.find(([reportCode]) => reportCode === 'crm.lead_conversion_funnel')?.[1]
    expect(funnelRequest.layout.rowGroups).toEqual([{ fieldCode: 'funnel_step' }])
    expect(funnelRequest.layout.detailFields).toEqual([])
    expect(funnelRequest.layout.showDetails).toBe(false)
  })

  it('reads funnel counts from grouped rows without requesting document detail fields', async () => {
    executeReportMock.mockImplementation((reportCode: string) => {
      switch (reportCode) {
        case 'crm.sales_pipeline':
          return Promise.resolve(report(['amount__sum', 'weighted_amount__sum'], [[0, 0]]))
        case 'crm.lead_conversion_funnel':
          return Promise.resolve(report(
            ['__row_hierarchy', 'lead_count__sum'],
            [
              ['01 Intake', 12],
              ['02 Qualified', 7],
              ['03 Converted', 4],
            ],
            {},
            'Group',
          ))
        case 'crm.activity_summary':
          return Promise.resolve(report(['activity_count__sum'], [[0]]))
        case 'crm.quote_register':
          return Promise.resolve(report(['amount__sum', 'quote_count__sum'], [[0, 0]]))
        default:
          throw new Error(`Unexpected report ${reportCode}`)
      }
    })

    const data = await loadHomeDashboard('2026-07-05')

    expect(data.leadCount).toBe(12)
    expect(data.qualifiedLeadCount).toBe(7)
    expect(data.convertedLeadCount).toBe(4)

    const funnelRequest = executeReportMock.mock.calls.find(([reportCode]) => reportCode === 'crm.lead_conversion_funnel')?.[1]
    expect(funnelRequest.layout.detailFields).not.toContain('document_id')
    expect(funnelRequest.layout.detailFields).not.toContain('document_display')
  })

  it('uses first-cell funnel labels and direct measure columns at report boundaries', async () => {
    executeReportMock.mockImplementation((reportCode: string) => {
      switch (reportCode) {
        case 'crm.sales_pipeline':
          return Promise.resolve(report(
            ['amount', 'weighted_amount'],
            [[125, 75]],
          ))
        case 'crm.lead_conversion_funnel':
          return Promise.resolve({
            sheet: {
              columns: [{ code: 'unknown' }, { code: 'lead_count' }],
              rows: [
                {
                  rowKind: 'Group',
                  cells: [
                    { value: '01 Imported', display: '01 Imported' },
                    { value: 9, display: '9' },
                  ],
                },
                {
                  rowKind: 'Group',
                  cells: undefined,
                },
              ],
            },
          })
        case 'crm.activity_summary':
        case 'crm.quote_register':
          return Promise.resolve({
            sheet: {
              columns: [],
              rows: undefined,
            },
          })
        default:
          throw new Error(`Unexpected report ${reportCode}`)
      }
    })

    const data = await loadHomeDashboard('2026-07-05')

    expect(data.pipelineAmount).toBe(125)
    expect(data.weightedPipelineAmount).toBe(75)
    expect(data.leadCount).toBe(9)
    expect(data.qualifiedLeadCount).toBe(0)
    expect(data.openOpportunities).toEqual([
      {
        opportunity: 'Opportunity',
        account: 'Account',
        stage: 'Stage',
        amount: 125,
        weightedAmount: 75,
        route: null,
      },
    ])

    executeReportMock.mockImplementation((reportCode: string) => {
      if (reportCode === 'crm.lead_conversion_funnel') {
        return Promise.resolve({
          sheet: {
            columns: [],
            rows: undefined,
          },
        })
      }
      return Promise.resolve(report([], []))
    })

    const emptyFunnel = await loadHomeDashboard('2026-07-05')
    expect(emptyFunnel.leadCount).toBe(0)
  })

  it('returns zeroed dashboard sections and warnings when report calls fail', async () => {
    executeReportMock.mockImplementation((reportCode: string) => {
      if (reportCode === 'crm.sales_pipeline') throw new Error('Pipeline offline')
      throw 'report unavailable'
    })

    const data = await loadHomeDashboard('2026-07-05')

    expect(data.pipelineAmount).toBe(0)
    expect(data.weightedPipelineAmount).toBe(0)
    expect(data.leadCount).toBe(0)
    expect(data.qualifiedLeadCount).toBe(0)
    expect(data.convertedLeadCount).toBe(0)
    expect(data.quoteAmount).toBe(0)
    expect(data.quoteCount).toBe(0)
    expect(data.activityCount).toBe(0)
    expect(data.openOpportunities).toEqual([])
    expect(data.warnings).toEqual(expect.arrayContaining([
      'crm.sales_pipeline: Pipeline offline',
      'crm.lead_conversion_funnel: Unable to load report data.',
      'crm.activity_summary: Unable to load report data.',
      'crm.quote_register: Unable to load report data.',
    ]))
  })
})
