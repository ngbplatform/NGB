import { DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH } from '../accounting/navigation'
import { NgbSourceKinds } from '../api/sourceKinds'
import { normalizeSingleQueryValue } from '../router/queryParams'
import { decodeReportRouteContextParam } from '../reporting/navigation'
import type { NgbNavigationLocation, NgbPageContext } from './types'

/** Adapter for the standard platform routes; applications may supply their own resolver. */
export function resolveDefaultPageContext(location: NgbNavigationLocation): NgbPageContext | null {
  const path = location.path.replace(/\/$/, '')
  const journalBase = DEFAULT_GENERAL_JOURNAL_ENTRIES_BASE_PATH
  if (path === journalBase || path.startsWith(`${journalBase}/`)) {
    const id = path === journalBase ? undefined : path.slice(journalBase.length + 1)
    return { kind: NgbSourceKinds.GeneralJournalEntry, typeCode: 'general_journal_entry', ...(id ? { id } : {}) }
  }

  const match = /^\/(catalogs|documents|reports)\/([^/]+)(?:\/([^/]+))?(?:\/(flow|effects|print))?$/.exec(path)
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
  if (match[4] && kind !== NgbSourceKinds.Document) return null
  if (kind === NgbSourceKinds.Report) {
    if (pathId) return null
    const context = decodeReportRouteContextParam(location.query.ctx)
    return context?.reportCode === typeCode
      ? { kind, typeCode, parameters: context.request.parameters, filters: context.request.filters }
      : { kind, typeCode }
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
