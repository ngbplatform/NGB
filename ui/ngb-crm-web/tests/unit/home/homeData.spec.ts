import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpGetMock = vi.hoisted(() => vi.fn())

vi.mock('@ngbplatform/ui', () => ({
  buildDocumentFullPageUrl: (typeCode: string, id: string) => `/documents/${typeCode}/${id}`,
  buildReportPageUrl: (reportCode: string) => `/reports/${reportCode}`,
  formatDashboardMonthLabel: (monthKey: string) => `label:${monthKey}`,
  httpGet: httpGetMock,
  parseDashboardUtcDateOnly: (value: unknown) => {
    const text = String(value ?? '')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return null
    const date = new Date(`${text}T00:00:00Z`)
    return Number.isNaN(date.getTime()) ? null : date
  },
  toDashboardUtcDateOnly: (date: Date) => date.toISOString().slice(0, 10),
  toDashboardUtcMonthKey: (date: Date) => date.toISOString().slice(0, 7),
}))

import { loadHomeDashboard } from '../../../src/home/homeData'

beforeEach(() => {
  httpGetMock.mockReset().mockResolvedValue({
    asOfUtc: '2026-07-05',
    pipelineAmount: 1_200,
    weightedPipelineAmount: 1_000,
    leadCount: 12,
    qualifiedLeadCount: 7,
    convertedLeadCount: 4,
    quoteAmount: 250,
    quoteCount: 3,
    activityCount: 4,
    openOpportunities: [
      {
        opportunityId: 'opp-1',
        opportunity: 'Enterprise CRM rollout',
        account: 'Acme Distribution',
        stage: 'Proposal',
        amount: 1_000,
        weightedAmount: 600,
      },
    ],
  })
})

describe('CRM home dashboard data', () => {
  it('maps the bounded aggregate response and preserves report drill-down routes', async () => {
    const data = await loadHomeDashboard('2026-07-05')

    expect(httpGetMock).toHaveBeenCalledOnce()
    expect(httpGetMock).toHaveBeenCalledWith('/api/dashboard', { asOfUtc: '2026-07-05' })
    expect(data).toMatchObject({
      asOf: '2026-07-05',
      monthKey: '2026-07',
      monthLabel: 'label:2026-07',
      pipelineAmount: 1_200,
      weightedPipelineAmount: 1_000,
      leadCount: 12,
      qualifiedLeadCount: 7,
      convertedLeadCount: 4,
      quoteAmount: 250,
      quoteCount: 3,
      activityCount: 4,
    })
    expect(data.openOpportunities).toEqual([
      expect.objectContaining({
        opportunity: 'Enterprise CRM rollout',
        route: '/documents/crm.lead_conversion/opp-1',
      }),
    ])
    expect(data.routes).toEqual({
      leads: '/documents/crm.lead_intake',
      pipeline: '/reports/crm.sales_pipeline',
      activities: '/reports/crm.activity_summary',
      quotes: '/reports/crm.quote_register',
      funnel: '/reports/crm.lead_conversion_funnel',
    })
  })

  it('forwards cancellation and tolerates an omitted opportunity list', async () => {
    httpGetMock.mockResolvedValue({
      asOfUtc: '2026-07-05',
      pipelineAmount: 0,
      weightedPipelineAmount: 0,
      leadCount: 0,
      qualifiedLeadCount: 0,
      convertedLeadCount: 0,
      quoteAmount: 0,
      quoteCount: 0,
      activityCount: 0,
    })
    const controller = new AbortController()

    const data = await loadHomeDashboard('2026-07-05', controller.signal)

    expect(data.openOpportunities).toEqual([])
    expect(httpGetMock).toHaveBeenCalledWith(
      '/api/dashboard',
      { asOfUtc: '2026-07-05' },
      { signal: controller.signal },
    )
  })

  it('rejects invalid dates before issuing an HTTP request', async () => {
    await expect(loadHomeDashboard('invalid')).rejects.toThrow('Select a valid as-of date.')
    expect(httpGetMock).not.toHaveBeenCalled()
  })
})
