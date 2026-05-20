import { operationSucceeded } from '../core/checks.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';
import { documentOpenFlow } from './documentOpenFlow.ts';

export function documentPostFlow(context: NgbScenarioContext, documentType: string, documentId?: string | null): boolean {
  const id = documentOpenFlow(context, documentType, documentId);
  if (!id || !context.env.enableWrites) {
    return false;
  }

  const response = context.documents.postDocument(documentType, id);
  return operationSucceeded(response, [200]);
}
