import { lookupHintFromSource } from 'ngb-ui-framework'
import type { LookupHint, LookupSource } from 'ngb-ui-framework'

const DIRECT_HINTS: Record<string, LookupHint> = {
  client_id: { kind: 'catalog', catalogType: 'ab.client' },
  team_member_id: { kind: 'catalog', catalogType: 'ab.team_member' },
  project_id: { kind: 'catalog', catalogType: 'ab.project' },
  project_manager_id: { kind: 'catalog', catalogType: 'ab.team_member' },
  service_item_id: { kind: 'catalog', catalogType: 'ab.service_item' },
  payment_terms_id: { kind: 'catalog', catalogType: 'ab.payment_terms' },
  contract_id: { kind: 'document', documentTypes: ['ab.client_contract'] },
  source_timesheet_id: { kind: 'document', documentTypes: ['ab.timesheet'] },
  sales_invoice_id: { kind: 'document', documentTypes: ['ab.sales_invoice'] },
}

export function getAgencyBillingLookupHint(
  _entityTypeCode: string,
  fieldKey: string,
  metaLookup?: LookupSource | null,
): LookupHint | null {
  const explicit = lookupHintFromSource(metaLookup)
  if (explicit) return explicit

  const key = fieldKey.trim().toLowerCase()
  if (key === 'account_id' || key.endsWith('_account_id')) {
    return { kind: 'coa' }
  }

  return DIRECT_HINTS[key] ?? null
}
