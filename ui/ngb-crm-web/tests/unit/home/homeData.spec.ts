import { beforeEach, describe, expect, it, vi } from 'vitest'

const executeReportMock = vi.hoisted(() => vi.fn())

vi.mock('ngb-ui-framework', () => ({
  ReportRowKind: {
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
  isDashboardReportRowKind: (row: any) => String(row.rowKind).toLowerCase() === 'detail',
  resolveReportCellActionUrl: (action: any) =>
    action?.kind === 'open_document' ? `/documents/${action.documentType}/${action.documentId}` : null,
  startOfDashboardUtcMonth: (date: Date) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)),
  toDashboardUtcDateOnly: (date: Date) => date.toISOString().slice(0, 10),
  toDashboardUtcMonthKey: (date: Date) =>
    `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`,
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

type CellValue = string | number

function report(columns: string[], rows: CellValue[][], actions: Record<string, any> = {}) {
  return {
    sheet: {
      columns: columns.map((code) => ({ code })),
      rows: rows.map((values) => ({
        rowKind: 'Detail',
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
            [['Enterprise CRM rollout', 'Acme Distribution', 'Proposal', 1000, 400]],
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

    expect(data.pipelineAmount).toBe(1000)
    expect(data.weightedPipelineAmount).toBe(400)
    expect(data.leadCount).toBe(2)
    expect(data.qualifiedLeadCount).toBe(1)
    expect(data.convertedLeadCount).toBe(1)
    expect(data.activityCount).toBe(4)
    expect(data.quoteAmount).toBe(250)
    expect(data.quoteCount).toBe(3)
    expect(data.openOpportunities).toEqual([
      {
        opportunity: 'Enterprise CRM rollout',
        account: 'Acme Distribution',
        stage: 'Proposal',
        amount: 1000,
        weightedAmount: 400,
        route: '/documents/crm.lead_conversion/opp-1',
      },
    ])
  })
})
