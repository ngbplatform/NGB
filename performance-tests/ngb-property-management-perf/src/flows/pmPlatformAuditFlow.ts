import { jsonHas, operationSucceeded } from '../../../ngb-performance-tests-framework/src/core/checks.ts';
import { documentOpenFlow, resolveFirstDocumentId } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { resolveFixtureId } from './pmFlowSupport.ts';

const AUDIT_DOCUMENT_TYPES = [
  PM_DOCUMENT_TYPES.lease,
  PM_DOCUMENT_TYPES.rentCharge,
  PM_DOCUMENT_TYPES.receivablePayment,
  PM_DOCUMENT_TYPES.maintenanceRequest,
] as const;

export function pmPlatformAuditFlow(context: NgbScenarioContext, documentId?: string | null): void {
  const id = documentId ?? resolveAuditDocumentId(context);
  if (!id) {
    return;
  }

  const response = context.audit.getEntityAuditLog('Document', id, { limit: 20 });
  operationSucceeded(response, [200]);
  jsonHas(response, 'items');
}

function resolveAuditDocumentId(context: NgbScenarioContext): string | null {
  const explicit = resolveFixtureId(
    'NGB_PM_FIXTURE_AUDIT_DOCUMENT_ID',
    'NGB_PM_FIXTURE_RENT_CHARGE_ID',
    'NGB_PM_FIXTURE_LEASE_ID',
  );
  if (explicit) {
    return explicit;
  }

  for (const documentType of AUDIT_DOCUMENT_TYPES) {
    const id = resolveFirstDocumentId(context, documentType);
    if (id) {
      documentOpenFlow(context, documentType, id);
      return id;
    }
  }

  return null;
}
