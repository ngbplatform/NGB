import type { AssistantSource } from './types'
import { NgbSourceKinds, isNgbSourceIdentity } from '../api/sourceKinds'
import { resolveNgbNavigationTarget } from '../navigation/config'

export function assistantSourcePath(source: AssistantSource): string | null {
  if (!isNgbSourceIdentity(source)) return null
  if (source.kind !== NgbSourceKinds.Report && !source.id) return null
  if (source.view != null && (source.view !== 'audit' || source.kind === NgbSourceKinds.Report)) return null

  const path = resolveNgbNavigationTarget({
    code: `${source.kind}.${source.kind === NgbSourceKinds.Report ? 'view' : 'editor'}`,
    parameters: {},
  }, { resourceKind: source.kind, resourceCode: source.typeCode, entityId: source.id })
  if (!path || !source.view) return path

  const target = new URL(path, window.location.origin)
  target.searchParams.set('view', source.view)
  return `${target.pathname}${target.search}${target.hash}`
}
