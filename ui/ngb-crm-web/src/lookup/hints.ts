import { lookupHintFromSource } from 'ngb-ui-framework'
import type { LookupHint, LookupSource } from 'ngb-ui-framework'

export function getCRMLookupHint(
  _entityTypeCode: string,
  _fieldKey: string,
  metaLookup?: LookupSource | null,
): LookupHint | null {
  return lookupHintFromSource(metaLookup)
}
