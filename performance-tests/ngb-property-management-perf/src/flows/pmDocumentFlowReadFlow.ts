import { documentFlowReadFlow } from '../../../ngb-performance-tests-framework/src/flows/documentFlowReadFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export function pmDocumentFlowReadFlow(context: NgbScenarioContext): void {
  const leaseId = __ENV.NGB_PM_FIXTURE_LEASE_ID?.trim() || null;
  documentFlowReadFlow(context, PM_DOCUMENT_TYPES.lease, leaseId);
}
