import { lookupHintFromSource } from '@ngbplatform/ui'
import type { LookupHint, LookupSource } from '@ngbplatform/ui'

export function getCRMLookupHint(
  _entityTypeCode: string,
  _fieldKey: string,
  metaLookup?: LookupSource | null,
): LookupHint | null {
  return lookupHintFromSource(metaLookup)
}
