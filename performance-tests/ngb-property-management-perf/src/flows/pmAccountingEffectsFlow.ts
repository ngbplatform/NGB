import { accountingEffectsFlow } from '../../../ngb-performance-tests-framework/src/flows/accountingEffectsFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export function pmAccountingEffectsFlow(context: NgbScenarioContext): void {
  const rentChargeId = __ENV.NGB_PM_FIXTURE_RENT_CHARGE_ID?.trim() || null;
  accountingEffectsFlow(context, PM_DOCUMENT_TYPES.rentCharge, rentChargeId);
}
