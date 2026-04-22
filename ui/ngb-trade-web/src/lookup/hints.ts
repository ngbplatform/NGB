import { lookupHintFromSource } from 'ngb-ui-framework'
import type { LookupHint, LookupSource } from 'ngb-ui-framework'

export function getTradeLookupHint(
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

  return null
}
