import { jsonHas, operationSucceeded } from '../core/checks.ts';
import type { NgbRequestTags } from '../core/requestTags.ts';
import { thinkTime } from '../core/sleep.ts';
import type { ReportExecutionRequest } from '../ngb/reportsClient.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function reportExecutionFlow(
  context: NgbScenarioContext,
  reportId: string,
  request: ReportExecutionRequest = {},
  tags: NgbRequestTags = {},
): void {
  const definition = context.reports.getReportDefinition(reportId);
  operationSucceeded(definition, [200]);
  thinkTime(0.2, 0.6);

  const response = context.reports.executeReport(reportId, request, tags);
  operationSucceeded(response, [200]);
  jsonHas(response, 'sheet');
}
