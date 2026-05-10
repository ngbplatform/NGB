import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import { accountingDateRangeRequest, asOfDateRequest, executeLeaseOptionalReport } from './pmFlowSupport.ts';

export function pmReportsFlow(context: NgbScenarioContext): void {
  reportExecutionFlow(context, PM_REPORT_IDS.trialBalance, accountingDateRangeRequest());
  reportExecutionFlow(context, PM_REPORT_IDS.generalJournal, accountingDateRangeRequest());
  reportExecutionFlow(context, PM_REPORT_IDS.occupancySummary, asOfDateRequest());
  executeLeaseOptionalReport(context, PM_REPORT_IDS.receivablesAging);
  executeLeaseOptionalReport(context, PM_REPORT_IDS.receivablesOpenItems);
}
