import { jsonHas, operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function documentListFlow(context: NgbScenarioContext, documentType: string, filters: Record<string, string> = {}): void {
  const response = context.documents.listDocuments(documentType, {
    offset: 0,
    limit: 20,
    filters,
  });

  operationSucceeded(response, [200]);
  jsonHas(response, 'items');
  thinkTime();
}
