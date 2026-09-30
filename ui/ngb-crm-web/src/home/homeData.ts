import {
  buildDocumentFullPageUrl,
  buildReportPageUrl,
  formatDashboardMonthLabel,
  httpGet,
  parseDashboardUtcDateOnly,
  toDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
} from '@ngbplatform/ui'

export type CrmHomePipelineItem = {
  opportunity: string
  account: string
  stage: string
  amount: number
  weightedAmount: number
  route: string | null
}

export type CrmHomeDashboardData = {
  warnings: string[]
  asOf: string
  monthKey: string
  monthLabel: string
  pipelineAmount: number
  weightedPipelineAmount: number
  leadCount: number
  qualifiedLeadCount: number
  convertedLeadCount: number
  quoteAmount: number
  quoteCount: number
  activityCount: number
  openOpportunities: CrmHomePipelineItem[]
  routes: {
    leads: string
    pipeline: string
    activities: string
    quotes: string
    funnel: string
  }
}

type CrmDashboardResponse = {
  asOfUtc: string
  pipelineAmount: number
  weightedPipelineAmount: number
  leadCount: number
  qualifiedLeadCount: number
  convertedLeadCount: number
  quoteAmount: number
  quoteCount: number
  activityCount: number
  openOpportunities: {
    opportunityId: string
    opportunity: string
    account: string
    stage: string
    amount: number
    weightedAmount: number
  }[]
}

const REPORTS = {
  pipeline: 'crm.sales_pipeline',
  funnel: 'crm.lead_conversion_funnel',
  activities: 'crm.activity_summary',
  quotes: 'crm.quote_register',
} as const

export async function loadHomeDashboard(asOf: string, signal?: AbortSignal): Promise<CrmHomeDashboardData> {
  const asOfDate = parseDashboardUtcDateOnly(asOf)
  if (!asOfDate) throw new Error('Select a valid as-of date.')

  const response = signal
    ? await httpGet<CrmDashboardResponse>('/api/dashboard', { asOfUtc: asOf }, { signal })
    : await httpGet<CrmDashboardResponse>('/api/dashboard', { asOfUtc: asOf })
  const monthKey = toDashboardUtcMonthKey(asOfDate)

  return {
    warnings: [],
    asOf: toDashboardUtcDateOnly(asOfDate),
    monthKey,
    monthLabel: formatDashboardMonthLabel(monthKey),
    pipelineAmount: response.pipelineAmount,
    weightedPipelineAmount: response.weightedPipelineAmount,
    leadCount: response.leadCount,
    qualifiedLeadCount: response.qualifiedLeadCount,
    convertedLeadCount: response.convertedLeadCount,
    quoteAmount: response.quoteAmount,
    quoteCount: response.quoteCount,
    activityCount: response.activityCount,
    openOpportunities: (response.openOpportunities ?? []).map((item) => ({
      opportunity: item.opportunity,
      account: item.account,
      stage: item.stage,
      amount: item.amount,
      weightedAmount: item.weightedAmount,
      route: buildDocumentFullPageUrl('crm.lead_conversion', item.opportunityId),
    })),
    routes: {
      leads: '/documents/crm.lead_intake',
      pipeline: buildReportPageUrl(REPORTS.pipeline),
      activities: buildReportPageUrl(REPORTS.activities),
      quotes: buildReportPageUrl(REPORTS.quotes),
      funnel: buildReportPageUrl(REPORTS.funnel),
    },
  }
}
