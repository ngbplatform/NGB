import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { documentOpenFlow } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export function pmReceivablePaymentApplyFlow(context: NgbScenarioContext): void {
  const fixtureId = __ENV.NGB_PM_FIXTURE_RECEIVABLE_PAYMENT_ID?.trim() || null;
  documentListFlow(context, PM_DOCUMENT_TYPES.receivablePayment);
  documentOpenFlow(context, PM_DOCUMENT_TYPES.receivablePayment, fixtureId);
}
