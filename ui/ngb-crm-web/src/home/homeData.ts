import {
  buildReportPageUrl,
  dashboardReportCellByCode,
  dashboardReportCellDisplay,
  dashboardReportCellNumber,
  dashboardReportColumnIndexMap,
  executeReport,
  formatDashboardMonthLabel,
  isDashboardReportRowKind,
  ReportRowKind,
  resolveReportCellActionUrl,
  startOfDashboardUtcMonth,
  toDashboardUtcDateOnly,
  toDashboardUtcMonthKey,
  type ReportExecutionRequestDto,
  type ReportExecutionResponseDto,
  type ReportSheetRowDto,
} from 'ngb-ui-framework'

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

const REPORTS = {
  pipeline: 'crm.sales_pipeline',
  funnel: 'crm.lead_conversion_funnel',
  activities: 'crm.activity_summary',
  quotes: 'crm.quote_register',
} as const

const DASHBOARD_REPORT_LIMIT = 2000

const DETAIL_REPORT_LAYOUTS: Record<string, ReportExecutionRequestDto> = {
  [REPORTS.pipeline]: {
    offset: 0,
    limit: DASHBOARD_REPORT_LIMIT,
    layout: {
      rowGroups: [],
      detailFields: [
        'opportunity_display',
        'customer_display',
        'stage_display',
        'status',
        'expected_close_date',
      ],
      measures: [
        { measureCode: 'amount' },
        { measureCode: 'weighted_amount' },
      ],
      showDetails: true,
      showGrandTotals: true,
    },
  },
  [REPORTS.funnel]: {
    offset: 0,
    limit: DASHBOARD_REPORT_LIMIT,
    layout: {
      rowGroups: [],
      detailFields: ['event_at_utc', 'funnel_step', 'lead_source', 'industry', 'document_id', 'document_display'],
      measures: [{ measureCode: 'lead_count' }],
      showDetails: true,
      showGrandTotals: true,
    },
  },
  [REPORTS.activities]: {
    offset: 0,
    limit: DASHBOARD_REPORT_LIMIT,
    layout: {
      rowGroups: [],
      detailFields: [
        'activity_date',
        'activity_type',
        'customer_display',
        'contact_display',
        'outcome',
      ],
      measures: [{ measureCode: 'activity_count' }],
      showDetails: true,
      showGrandTotals: true,
    },
  },
  [REPORTS.quotes]: {
    offset: 0,
    limit: DASHBOARD_REPORT_LIMIT,
    layout: {
      rowGroups: [],
      detailFields: [
        'quote_date',
        'quote_status',
        'customer_display',
        'contact_display',
        'currency',
      ],
      measures: [
        { measureCode: 'amount' },
        { measureCode: 'quote_count' },
      ],
      showDetails: true,
      showGrandTotals: true,
    },
  },
}

function detailRows(response: ReportExecutionResponseDto): ReportSheetRowDto[] {
  return (response.sheet.rows ?? []).filter((row) => isDashboardReportRowKind(row, ReportRowKind.Detail))
}

async function safeExecuteReport(
  reportCode: string,
  warnings: string[],
  request: ReportExecutionRequestDto = { offset: 0, limit: DASHBOARD_REPORT_LIMIT },
): Promise<ReportExecutionResponseDto | null> {
  try {
    return await executeReport(reportCode, request)
  } catch (error) {
    warnings.push(`${reportCode}: ${error instanceof Error ? error.message : 'Unable to load report data.'}`)
    return null
  }
}

function sumColumn(response: ReportExecutionResponseDto | null, columnKey: string): number {
  if (!response) return 0
  const columns = dashboardReportColumnIndexMap(response)
  return detailRows(response).reduce((sum, row) => sum + measureCellNumber(row, columns, columnKey), 0)
}

function funnelCount(response: ReportExecutionResponseDto | null, stepPrefix: string): number {
  if (!response) return 0
  const columns = dashboardReportColumnIndexMap(response)
  return detailRows(response)
    .filter((row) => dashboardReportCellDisplay(row, columns, 'funnel_step').startsWith(stepPrefix))
    .reduce((sum, row) => sum + measureCellNumber(row, columns, 'lead_count'), 0)
}

function measureCellNumber(row: ReportSheetRowDto, columns: Map<string, number>, measureCode: string): number {
  const direct = dashboardReportCellNumber(row, columns, measureCode)
  return direct || dashboardReportCellNumber(row, columns, `${measureCode}__sum`)
}

function topPipelineItems(response: ReportExecutionResponseDto | null): CrmHomePipelineItem[] {
  if (!response) return []
  const columns = dashboardReportColumnIndexMap(response)

  return detailRows(response)
    .map((row) => {
      const opportunity = dashboardReportCellDisplay(row, columns, 'opportunity_display')
      const account = dashboardReportCellDisplay(row, columns, 'customer_display')
      const stage = dashboardReportCellDisplay(row, columns, 'stage_display')
      const amount = measureCellNumber(row, columns, 'amount')
      const weightedAmount = measureCellNumber(row, columns, 'weighted_amount')
      const opportunityRoute = resolveReportCellActionUrl(dashboardReportCellByCode(row, columns, 'opportunity_display')?.action)

      return {
        opportunity: opportunity || 'Opportunity',
        account: account || 'Account',
        stage: stage || 'Stage',
        amount,
        weightedAmount,
        route: opportunityRoute,
      }
    })
    .filter((item) => item.amount > 0 || item.weightedAmount > 0)
    .sort((left, right) => right.weightedAmount - left.weightedAmount)
    .slice(0, 6)
}

export async function loadHomeDashboard(asOf: string): Promise<CrmHomeDashboardData> {
  const warnings: string[] = []
  const asOfDate = new Date(`${asOf}T00:00:00.000Z`)
  const start = startOfDashboardUtcMonth(asOfDate)
  const monthKey = toDashboardUtcMonthKey(start)
  const monthLabel = formatDashboardMonthLabel(monthKey)

  const [pipeline, funnel, activities, quotes] = await Promise.all([
    safeExecuteReport(REPORTS.pipeline, warnings, DETAIL_REPORT_LAYOUTS[REPORTS.pipeline]),
    safeExecuteReport(REPORTS.funnel, warnings, DETAIL_REPORT_LAYOUTS[REPORTS.funnel]),
    safeExecuteReport(REPORTS.activities, warnings, DETAIL_REPORT_LAYOUTS[REPORTS.activities]),
    safeExecuteReport(REPORTS.quotes, warnings, DETAIL_REPORT_LAYOUTS[REPORTS.quotes]),
  ])

  return {
    warnings,
    asOf: toDashboardUtcDateOnly(asOfDate),
    monthKey,
    monthLabel,
    pipelineAmount: sumColumn(pipeline, 'amount'),
    weightedPipelineAmount: sumColumn(pipeline, 'weighted_amount'),
    leadCount: funnelCount(funnel, '01'),
    qualifiedLeadCount: funnelCount(funnel, '02'),
    convertedLeadCount: funnelCount(funnel, '03'),
    quoteAmount: sumColumn(quotes, 'amount'),
    quoteCount: sumColumn(quotes, 'quote_count'),
    activityCount: sumColumn(activities, 'activity_count'),
    openOpportunities: topPipelineItems(pipeline),
    routes: {
      leads: '/documents/crm.lead_intake',
      pipeline: buildReportPageUrl(REPORTS.pipeline),
      activities: buildReportPageUrl(REPORTS.activities),
      quotes: buildReportPageUrl(REPORTS.quotes),
      funnel: buildReportPageUrl(REPORTS.funnel),
    },
  }
}
