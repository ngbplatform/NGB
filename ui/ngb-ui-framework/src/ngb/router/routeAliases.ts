import type { RouteLocationNormalizedLoaded, RouteLocationRaw, RouteRecordRaw } from 'vue-router'
import { buildGeneralJournalEntriesListPath, buildGeneralJournalEntriesPath } from '../accounting/navigation'
import { buildReportPageUrl } from '../reporting/navigation'

type AliasRouteLike = Pick<RouteLocationNormalizedLoaded, 'params' | 'query' | 'hash'>

function preserveAliasContext(to: AliasRouteLike, path: string): RouteLocationRaw {
  return {
    path,
    query: to.query,
    hash: to.hash,
  }
}

export const ngbRouteAliasRedirectRoutes: RouteRecordRaw[] = [
  {
    path: '/documents/general_journal_entry',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesListPath()),
  },
  {
    path: '/documents/general_journal_entry/new',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesPath()),
  },
  {
    path: '/documents/general_journal_entry/:id',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesPath(String(to.params.id ?? ''))),
  },
  {
    path: '/documents/accounting.general_journal_entry',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesListPath()),
  },
  {
    path: '/documents/accounting.general_journal_entry/new',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesPath()),
  },
  {
    path: '/documents/accounting.general_journal_entry/:id',
    redirect: to => preserveAliasContext(to, buildGeneralJournalEntriesPath(String(to.params.id ?? ''))),
  },
  {
    path: '/admin/accounting/posting-log',
    redirect: to => preserveAliasContext(to, buildReportPageUrl('accounting.posting_log')),
  },
  {
    path: '/admin/accounting/consistency',
    redirect: to => preserveAliasContext(to, buildReportPageUrl('accounting.consistency')),
  },
]

export function normalizeNgbRouteAliasPath(path: string | null | undefined): string {
  const value = String(path ?? '').trim()
  if (!value) return ''

  if (value.startsWith('/documents/accounting.general_journal_entry')) {
    return value.replace('/documents/accounting.general_journal_entry', '/accounting/general-journal-entries')
  }

  if (value.startsWith('/documents/general_journal_entry')) {
    return value.replace('/documents/general_journal_entry', '/accounting/general-journal-entries')
  }

  if (value === '/admin/accounting/posting-log') return buildReportPageUrl('accounting.posting_log')
  if (value === '/admin/accounting/consistency') return buildReportPageUrl('accounting.consistency')

  return value
}
