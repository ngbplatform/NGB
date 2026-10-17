import { DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH } from '../accounting/navigation'
import { NgbSourceKinds, type NgbSourceIdentity } from '../api/sourceKinds'
import { normalizeSingleQueryValue } from '../router/queryParams'
import type { NgbNavigationLocation } from './types'

/** Adapter for the standard platform routes; applications may supply their own resolver. */
export function resolveDefaultPageContext(location: NgbNavigationLocation): NgbSourceIdentity | null {
  const path = location.path.replace(/\/$/, '')
  const journalBase = DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH
  if (path === journalBase || path.startsWith(`${journalBase}/`)) {
    const id = path === journalBase ? undefined : path.slice(journalBase.length + 1)
    return { kind: NgbSourceKinds.GeneralJournalEntry, typeCode: 'general_journal_entry', ...(id ? { id } : {}) }
  }

  const match = /^\/(catalogs|documents|reports)\/([^/]+)(?:\/([^/]+))?$/.exec(path)
  if (!match) return null

  let typeCode: string
  try {
    typeCode = decodeURIComponent(match[2]!)
  } catch {
    return null
  }

  const kind = match[1] === 'catalogs'
    ? NgbSourceKinds.Catalog
    : match[1] === 'documents' ? NgbSourceKinds.Document : NgbSourceKinds.Report
  const pathId = match[3]
  if (kind === NgbSourceKinds.Report) {
    return pathId ? null : { kind, typeCode }
  }

  if (pathId) return { kind, typeCode, id: pathId }

  const panel = normalizeSingleQueryValue(location.query.panel).toLowerCase()
  if (panel === 'new') return null
  if (panel === 'edit') {
    const id = normalizeSingleQueryValue(location.query.id)
    return id ? { kind, typeCode, id } : null
  }

  return { kind, typeCode }
}
