import type { AssistantPageContext, AssistantSource } from './types'
import { NgbSourceKinds, isNgbSourceKind } from '../api/sourceKinds'
import { resolveNgbNavigationTarget } from '../navigation/config'
import { DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH } from '../accounting/navigation'

const identifier = /^[a-zA-Z0-9_.-]{1,128}$/
const recordId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function assistantPageContext(path: string): AssistantPageContext | null {
  const journalBase = DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH
  if (path === journalBase) return { kind: NgbSourceKinds.GeneralJournalEntry, typeCode: 'general_journal_entry' }
  if (path.startsWith(`${journalBase}/`)) {
    const id = path.slice(journalBase.length + 1)
    return recordId.test(id) ? { kind: NgbSourceKinds.GeneralJournalEntry, typeCode: 'general_journal_entry', id } : null
  }
  const match = /^\/(catalogs|documents|reports)\/([^/]+)(?:\/([^/]+))?\/?$/.exec(path)
  if (!match) return null
  let typeCode: string
  try {
    typeCode = decodeURIComponent(match[2]!)
  } catch {
    return null
  }
  if (!identifier.test(typeCode)) return null
  const kind = match[1] === 'catalogs'
    ? NgbSourceKinds.Catalog
    : match[1] === 'documents' ? NgbSourceKinds.Document : NgbSourceKinds.Report
  const id = match[3]
  if (id && !recordId.test(id)) return null
  return { kind, typeCode, ...(id ? { id } : {}) }
}

export function assistantSourcePath(source: AssistantSource): string | null {
  if (!source || !identifier.test(source.typeCode)) return null
  if (!isNgbSourceKind(source.kind)) return null
  if (source.kind !== NgbSourceKinds.Report && (!source.id || !recordId.test(source.id))) return null
  return resolveNgbNavigationTarget({
    code: `${source.kind}.${source.kind === NgbSourceKinds.Report ? 'view' : 'editor'}`,
    parameters: {},
  }, { resourceKind: source.kind, resourceCode: source.typeCode, entityId: source.id })
}
