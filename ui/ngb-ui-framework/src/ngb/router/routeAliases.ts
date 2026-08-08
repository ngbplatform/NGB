import type { RouteLocationNormalizedLoaded, RouteLocationRaw, RouteRecordRaw } from 'vue-router'

const GENERAL_JOURNAL_ENTRIES_PATH = '/accounting/general-journal-entries'

function generalJournalEntryPath(id?: string | null): string {
  const normalizedId = String(id ?? '').trim()
  return normalizedId
    ? `${GENERAL_JOURNAL_ENTRIES_PATH}/${encodeURIComponent(normalizedId)}`
    : `${GENERAL_JOURNAL_ENTRIES_PATH}/new`
}

function reportPath(reportCode: string): string {
  return `/reports/${encodeURIComponent(reportCode)}`
}

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
    redirect: to => preserveAliasContext(to, GENERAL_JOURNAL_ENTRIES_PATH),
  },
  {
    path: '/documents/general_journal_entry/new',
    redirect: to => preserveAliasContext(to, generalJournalEntryPath()),
  },
  {
    path: '/documents/general_journal_entry/:id',
    redirect: to => preserveAliasContext(to, generalJournalEntryPath(String(to.params.id ?? ''))),
  },
  {
    path: '/documents/accounting.general_journal_entry',
    redirect: to => preserveAliasContext(to, GENERAL_JOURNAL_ENTRIES_PATH),
  },
  {
    path: '/documents/accounting.general_journal_entry/new',
    redirect: to => preserveAliasContext(to, generalJournalEntryPath()),
  },
  {
    path: '/documents/accounting.general_journal_entry/:id',
    redirect: to => preserveAliasContext(to, generalJournalEntryPath(String(to.params.id ?? ''))),
  },
  {
    path: '/admin/accounting/posting-log',
    redirect: to => preserveAliasContext(to, reportPath('accounting.posting_log')),
  },
  {
    path: '/admin/accounting/consistency',
    redirect: to => preserveAliasContext(to, reportPath('accounting.consistency')),
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

  if (value === '/admin/accounting/posting-log') return reportPath('accounting.posting_log')
  if (value === '/admin/accounting/consistency') return reportPath('accounting.consistency')

  return value
}
