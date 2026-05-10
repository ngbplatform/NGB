import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import { accountingDateRangeRequest, executeLeaseOptionalReport } from '../flows/pmFlowSupport.ts';

export const options = buildBaselineProfile({
  exec: 'reportingRegression',
  scenarioName: 'reporting_regression',
  tags: { vertical: 'property-management', scenario: 'pm.reporting_regression' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function reportingRegression(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  reportExecutionFlow(context, PM_REPORT_IDS.trialBalance, accountingDateRangeRequest());
  reportExecutionFlow(context, PM_REPORT_IDS.generalJournal, accountingDateRangeRequest());
  context.reports.getReportDefinition(PM_REPORT_IDS.accountCard);
  executeLeaseOptionalReport(context, PM_REPORT_IDS.receivablesAging);
  executeLeaseOptionalReport(context, PM_REPORT_IDS.receivablesOpenItems);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
