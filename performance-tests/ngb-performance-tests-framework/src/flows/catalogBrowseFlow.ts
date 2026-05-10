import { jsonHas, operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function catalogBrowseFlow(context: NgbScenarioContext, catalogType: string, search?: string): void {
  const response = context.catalogs.listCatalogItems(catalogType, {
    offset: 0,
    limit: 20,
    ...(search ? { search } : {}),
  });

  operationSucceeded(response, [200]);
  jsonHas(response, 'items');
  thinkTime();

  const firstId = firstPageItemId(response);
  if (firstId) {
    context.catalogs.openCatalogItem(catalogType, firstId);
  }
}

function firstPageItemId(response: { json(): unknown }): string | null {
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
