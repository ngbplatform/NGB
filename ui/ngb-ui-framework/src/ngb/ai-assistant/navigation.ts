import type { AssistantSource } from './types'
import { NgbSourceKinds, isNgbSourceIdentity } from '../api/sourceKinds'
import { resolveNgbNavigationTarget } from '../navigation/config'

export function assistantSourcePath(source: AssistantSource): string | null {
  if (!isNgbSourceIdentity(source)) return null
  if (source.kind !== NgbSourceKinds.Report && !source.id) return null

  return resolveNgbNavigationTarget({
    code: `${source.kind}.${source.kind === NgbSourceKinds.Report ? 'view' : 'editor'}`,
    parameters: {},
  }, { resourceKind: source.kind, resourceCode: source.typeCode, entityId: source.id })
}
