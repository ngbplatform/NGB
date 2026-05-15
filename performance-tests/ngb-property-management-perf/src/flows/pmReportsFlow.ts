import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { ReportExecutionRequest } from '../../../ngb-performance-tests-framework/src/ngb/reportsClient.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import {
  accountFilteredAccountingDateRangeRequest,
  accountingDateRangeRequest,
  accountingPeriodRequest,
  asOfDateRequest,
  type PmPeriodProfile,
  readBooleanEnv,
  reportTags,
  resolveAccountId,
} from './pmFlowSupport.ts';

export interface PmReportsFlowOptions {
  readonly periodProfiles?: readonly PmPeriodProfile[];
  readonly includeAccountScopedReports?: boolean;
  readonly includeLedgerAnalysisVariants?: boolean;
  readonly includeXlsxExport?: boolean;
}

export function pmReportsFlow(context: NgbScenarioContext, options: PmReportsFlowOptions = {}): void {
  const profiles = options.periodProfiles ?? ['open'];
  const includeAccountScopedReports = options.includeAccountScopedReports ?? true;
  const includeLedgerAnalysisVariants = options.includeLedgerAnalysisVariants ?? false;
  const includeXlsxExport = options.includeXlsxExport ?? false;

  for (const profile of profiles) {
    executeCanonicalAccountingReports(context, profile);
    executeLedgerAnalysis(context, profile, includeLedgerAnalysisVariants);
    executeAuditFriendlyReports(context, profile);

    if (includeAccountScopedReports) {
      executeAccountScopedReports(context, profile);
    }

    if (includeXlsxExport) {
      context.reports.exportXlsx(PM_REPORT_IDS.ledgerAnalysis, ledgerAnalysisGroupedRequest(profile, 500), reportTags(profile));
    }
  }
}

function executeCanonicalAccountingReports(context: NgbScenarioContext, profile: PmPeriodProfile): void {
  reportExecutionFlow(context, PM_REPORT_IDS.trialBalance, accountingDateRangeRequest(profile), reportTags(profile));
  reportExecutionFlow(context, PM_REPORT_IDS.balanceSheet, asOfDateRequest(profile), reportTags(profile));
  reportExecutionFlow(context, PM_REPORT_IDS.incomeStatement, accountingDateRangeRequest(profile), reportTags(profile));
  if (profile === 'open' || readBooleanEnv('NGB_PERF_ENABLE_EXTENDED_CASH_FLOW', false)) {
    reportExecutionFlow(context, PM_REPORT_IDS.cashFlowStatementIndirect, accountingDateRangeRequest(profile), reportTags(profile));
  }
  reportExecutionFlow(context, PM_REPORT_IDS.statementOfChangesInEquity, accountingDateRangeRequest(profile), reportTags(profile));
  reportExecutionFlow(context, PM_REPORT_IDS.generalJournal, accountingDateRangeRequest(profile, 100), reportTags(profile));
}

function executeLedgerAnalysis(
  context: NgbScenarioContext,
  profile: PmPeriodProfile,
  includeVariants: boolean,
): void {
  reportExecutionFlow(context, PM_REPORT_IDS.ledgerAnalysis, ledgerAnalysisGroupedRequest(profile), reportTags(profile));

  if (!includeVariants) {
    return;
  }

  context.reports.listVariants(PM_REPORT_IDS.ledgerAnalysis);
  reportExecutionFlow(context, PM_REPORT_IDS.ledgerAnalysis, ledgerAnalysisFlatDetailRequest(profile), {
    ...reportTags(profile),
    scenario: 'pm.platform_reporting.ledger_flat_detail',
  });
  reportExecutionFlow(context, PM_REPORT_IDS.ledgerAnalysis, ledgerAnalysisPivotRequest(profile), {
    ...reportTags(profile),
    scenario: 'pm.platform_reporting.ledger_pivot',
  });
}

function executeAuditFriendlyReports(context: NgbScenarioContext, profile: PmPeriodProfile): void {
  reportExecutionFlow(context, PM_REPORT_IDS.postingLog, {}, reportTags(profile));
  reportExecutionFlow(context, PM_REPORT_IDS.consistency, accountingPeriodRequest(profile), reportTags(profile));
}

function executeAccountScopedReports(context: NgbScenarioContext, profile: PmPeriodProfile): void {
  const accountId = resolveAccountId();
  if (accountId) {
    const accountRequest = accountFilteredAccountingDateRangeRequest(accountId, profile, 100);
    reportExecutionFlow(context, PM_REPORT_IDS.accountCard, accountRequest, reportTags(profile));
    reportExecutionFlow(context, PM_REPORT_IDS.generalLedgerAggregated, accountRequest, reportTags(profile));
    return;
  }

  context.reports.getReportDefinition(PM_REPORT_IDS.accountCard);
  context.reports.getReportDefinition(PM_REPORT_IDS.generalLedgerAggregated);
}

export function ledgerAnalysisGroupedRequest(profile: PmPeriodProfile, limit = 200): ReportExecutionRequest {
  return {
    ...accountingDateRangeRequest(profile, limit),
    layout: {
      rowGroups: [
        { fieldCode: 'account_display' },
        { fieldCode: 'period_utc', timeGrain: 'Month' },
      ],
      measures: [
        { measureCode: 'debit_amount', aggregation: 'Sum' },
        { measureCode: 'credit_amount', aggregation: 'Sum' },
      ],
      sorts: [
        { fieldCode: 'account_display', direction: 'Asc' },
        { fieldCode: 'period_utc', direction: 'Asc', timeGrain: 'Month' },
      ],
      showDetails: false,
      showSubtotals: true,
      showSubtotalsOnSeparateRows: false,
      showGrandTotals: true,
    },
  };
}

export function ledgerAnalysisFlatDetailRequest(profile: PmPeriodProfile, limit = 100): ReportExecutionRequest {
  return {
    ...accountingDateRangeRequest(profile, limit),
    layout: {
      detailFields: [
        'period_utc',
        'account_display',
        'document_display',
      ],
      measures: [
        { measureCode: 'debit_amount', aggregation: 'Sum' },
        { measureCode: 'credit_amount', aggregation: 'Sum' },
        { measureCode: 'net_amount', aggregation: 'Sum' },
      ],
      sorts: [
        { fieldCode: 'period_utc', direction: 'Asc' },
        { fieldCode: 'account_display', direction: 'Asc' },
      ],
      showDetails: false,
      showSubtotals: false,
      showGrandTotals: false,
    },
  };
}

export function ledgerAnalysisPivotRequest(profile: PmPeriodProfile, limit = 200): ReportExecutionRequest {
  return {
    ...accountingDateRangeRequest(profile, limit),
    layout: {
      rowGroups: [
        { fieldCode: 'account_display' },
      ],
      columnGroups: [
        { fieldCode: 'period_utc', timeGrain: 'Month' },
      ],
      measures: [
        { measureCode: 'debit_amount', aggregation: 'Sum' },
        { measureCode: 'credit_amount', aggregation: 'Sum' },
      ],
      sorts: [
        { fieldCode: 'account_display', direction: 'Asc' },
        { fieldCode: 'period_utc', direction: 'Asc', timeGrain: 'Month', appliesToColumnAxis: true },
      ],
      showDetails: false,
      showSubtotals: true,
      showGrandTotals: true,
    },
  };
}
