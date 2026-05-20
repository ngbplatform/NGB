import { catalogBrowseFlow } from '../../../ngb-performance-tests-framework/src/flows/catalogBrowseFlow.ts';
import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { documentOpenFlow } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_CATALOG_TYPES } from '../clients/pmCatalogTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export function pmLeaseBrowseFlow(context: NgbScenarioContext): void {
  catalogBrowseFlow(context, PM_CATALOG_TYPES.property);
  catalogBrowseFlow(context, PM_CATALOG_TYPES.party);
  documentListFlow(context, PM_DOCUMENT_TYPES.lease);
  documentOpenFlow(context, PM_DOCUMENT_TYPES.lease);
}
