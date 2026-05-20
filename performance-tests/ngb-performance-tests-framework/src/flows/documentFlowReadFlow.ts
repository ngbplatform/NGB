import { operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';
import { resolveFirstDocumentId } from './documentOpenFlow.ts';

export function documentFlowReadFlow(context: NgbScenarioContext, documentType: string, documentId?: string | null): void {
  const id = documentId ?? resolveFirstDocumentId(context, documentType);
  if (!id) {
    return;
  }

  const response = context.documents.getDocumentFlow(documentType, id);
  operationSucceeded(response, [200]);
  thinkTime();
}
