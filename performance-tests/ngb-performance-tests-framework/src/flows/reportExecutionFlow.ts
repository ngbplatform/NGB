import { jsonHas, operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { ReportExecutionRequest } from '../ngb/reportsClient.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function reportExecutionFlow(
  context: NgbScenarioContext,
  reportId: string,
  request: ReportExecutionRequest = {},
): void {
  const definition = context.reports.getReportDefinition(reportId);
  operationSucceeded(definition, [200]);
  thinkTime(0.2, 0.6);

  const response = context.reports.executeReport(reportId, request);
  operationSucceeded(response, [200]);
  jsonHas(response, 'sheet');
}
