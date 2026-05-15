import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { documentPostFlow } from '../../../ngb-performance-tests-framework/src/flows/documentPostFlow.ts';
import { accountingEffectsFlow } from '../../../ngb-performance-tests-framework/src/flows/accountingEffectsFlow.ts';
import { documentFlowReadFlow } from '../../../ngb-performance-tests-framework/src/flows/documentFlowReadFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { postingEnabled } from './pmFlowSupport.ts';

export function pmRentChargePostingFlow(context: NgbScenarioContext): void {
  const fixtureId = __ENV.NGB_PM_FIXTURE_RENT_CHARGE_ID?.trim() || null;
  documentListFlow(context, PM_DOCUMENT_TYPES.rentCharge);
  if (postingEnabled(context)) {
    documentPostFlow(context, PM_DOCUMENT_TYPES.rentCharge, fixtureId);
  }

  accountingEffectsFlow(context, PM_DOCUMENT_TYPES.rentCharge, fixtureId);
  documentFlowReadFlow(context, PM_DOCUMENT_TYPES.rentCharge, fixtureId);
}
