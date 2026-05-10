import { catalogBrowseFlow } from '../../../ngb-performance-tests-framework/src/flows/catalogBrowseFlow.ts';
import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { platformSmokeFlow } from '../../../ngb-performance-tests-framework/src/flows/platformSmokeFlow.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_CATALOG_TYPES } from '../clients/pmCatalogTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import { accountingDateRangeRequest, asOfDateRequest } from './pmFlowSupport.ts';

export function pmSmokeFlow(context: NgbScenarioContext): void {
  platformSmokeFlow(context);
  catalogBrowseFlow(context, PM_CATALOG_TYPES.property);
  documentListFlow(context, PM_DOCUMENT_TYPES.lease);
  context.reports.listReports();
  context.reports.getReportDefinition(PM_REPORT_IDS.trialBalance);
  reportExecutionFlow(context, PM_REPORT_IDS.trialBalance, accountingDateRangeRequest());
  reportExecutionFlow(context, PM_REPORT_IDS.occupancySummary, asOfDateRequest());
}
