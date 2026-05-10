import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import { asOfDateRequest, currentDateOnly, currentMonthStart } from './pmFlowSupport.ts';

export function pmDashboardFlow(context: NgbScenarioContext): void {
  reportExecutionFlow(context, PM_REPORT_IDS.occupancySummary, asOfDateRequest());
  documentListFlow(context, PM_DOCUMENT_TYPES.lease);
  documentListFlow(context, PM_DOCUMENT_TYPES.rentCharge, {
    periodFrom: currentMonthStart(),
    periodTo: currentDateOnly(),
  });
  documentListFlow(context, PM_DOCUMENT_TYPES.receivablePayment, {
    periodFrom: currentMonthStart(),
    periodTo: currentDateOnly(),
  });
}
