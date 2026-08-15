import { catalogBrowseFlow } from '../../../ngb-performance-tests-framework/src/flows/catalogBrowseFlow.ts';
import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { platformSmokeFlow } from '../../../ngb-performance-tests-framework/src/flows/platformSmokeFlow.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import { operationSucceeded } from '../../../ngb-performance-tests-framework/src/core/checks.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_CATALOG_TYPES } from '../clients/pmCatalogTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { PM_REPORT_IDS } from '../clients/pmReportIds.ts';
import { accountingDateRangeRequest, reportTags } from './pmFlowSupport.ts';

export function pmSmokeFlow(context: NgbScenarioContext): void {
  platformSmokeFlow(context);
  operationSucceeded(context.workCenter.getSummary(), [200]);
  operationSucceeded(context.workCenter.getItems({ limit: 1 }), [200]);
  operationSucceeded(context.workCenter.getItems({ limit: 50 }), [200]);
  operationSucceeded(context.workCenter.getNotificationPreferences(), [200]);
  catalogBrowseFlow(context, PM_CATALOG_TYPES.property);
  documentListFlow(context, PM_DOCUMENT_TYPES.lease);
  context.reports.listReports();
  context.reports.getReportDefinition(PM_REPORT_IDS.trialBalance);
  context.admin.getMainMenu();
  context.admin.getChartOfAccountsMetadata();
  reportExecutionFlow(context, PM_REPORT_IDS.trialBalance, accountingDateRangeRequest('open'), reportTags('open'));
  reportExecutionFlow(context, PM_REPORT_IDS.ledgerAnalysis, accountingDateRangeRequest('open'), reportTags('open'));
}
