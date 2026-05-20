import { operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function documentOpenFlow(context: NgbScenarioContext, documentType: string, documentId?: string | null): string | null {
  const id = documentId ?? resolveFirstDocumentId(context, documentType);
  if (!id) {
    return null;
  }

  const response = context.documents.openDocument(documentType, id);
  operationSucceeded(response, [200]);
  thinkTime();
  return id;
}

export function resolveFirstDocumentId(context: NgbScenarioContext, documentType: string): string | null {
  const response = context.documents.listDocuments(documentType, {
    offset: 0,
    limit: 1,
  });

  try {
    const json = response.json();
    const items = typeof json === 'object' && json !== null
      ? (json as { items?: Array<{ id?: unknown }> }).items
      : undefined;
    const id = items?.[0]?.id;
    return typeof id === 'string' ? id : null;
  } catch {
    return null;
  }
}
