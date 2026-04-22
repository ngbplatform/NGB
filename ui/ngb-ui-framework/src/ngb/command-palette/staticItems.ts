import { buildAccountingPeriodClosingPath, buildChartOfAccountsPath, buildGeneralJournalEntriesPath } from '../accounting/navigation'
import { buildCatalogFullPageUrl } from '../editor/catalogNavigation'
import { buildDocumentEffectsPageUrl, buildDocumentFlowPageUrl, buildDocumentFullPageUrl } from '../editor/documentNavigation'
import { buildReportPageUrl } from '../reporting/navigation'
import type {
  CommandPaletteGroupCode,
  CommandPaletteItemSeed,
  CommandPaletteScope,
} from './types'
import type { NgbIconName } from '../primitives/iconNames'

export type BuildNgbHeuristicCurrentActionsOptions = {
  excludedCatalogTypes?: string[]
}

function createRouteAction(
  key: string,
  title: string,
  route: string,
  options?: Partial<CommandPaletteItemSeed>,
): CommandPaletteItemSeed {
  return {
    key,
    group: 'actions',
    kind: 'command',
    scope: 'commands',
    title,
    subtitle: options?.subtitle ?? null,
    icon: options?.icon ?? 'arrow-right',
    badge: options?.badge ?? 'Action',
    hint: options?.hint ?? null,
    route,
    commandCode: options?.commandCode ?? null,
    status: options?.status ?? null,
    openInNewTabSupported: options?.openInNewTabSupported ?? true,
    keywords: options?.keywords ?? [],
    defaultRank: options?.defaultRank ?? 980,
    isCurrentContext: true,
  }
}

function createStaticCreateItem(key: string, title: string, route: string, keywords: string[]): CommandPaletteItemSeed {
  return {
    key,
    group: 'actions',
    kind: 'command',
    scope: 'commands',
    title,
    subtitle: 'Create a new record',
    icon: 'plus',
    badge: 'Create',
    hint: null,
    route,
    commandCode: key,
    status: null,
    openInNewTabSupported: true,
    keywords: ['create', 'new', ...keywords],
    defaultRank: 0,
  }
}

function createStaticPageItem(
  key: string,
  title: string,
  route: string,
  icon: NgbIconName,
  keywords: string[],
  subtitle: string,
): CommandPaletteItemSeed {
  return {
    key,
    group: 'go-to' as CommandPaletteGroupCode,
    kind: 'page',
    scope: 'pages' as CommandPaletteScope,
    title,
    subtitle,
    icon,
    badge: 'Page',
    hint: null,
    route,
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords,
    defaultRank: 0,
  }
}

function toUrl(route: string): URL {
  return new URL(route || '/', 'https://ngb.local')
}

export function buildNgbHeuristicCurrentActions(
  fullRoute: string,
  options: BuildNgbHeuristicCurrentActionsOptions = {},
): CommandPaletteItemSeed[] {
  const url = toUrl(fullRoute)
  const path = url.pathname
  const items: CommandPaletteItemSeed[] = []
  const excludedCatalogTypes = new Set(options.excludedCatalogTypes ?? [])

  const documentMatch = path.match(/^\/documents\/([^/]+)\/([^/]+)$/)
  if (documentMatch) {
    const [, documentType, entityId] = documentMatch
    if (entityId !== 'new') {
      items.push(
        createRouteAction(`current:flow:${documentType}:${entityId}`, 'Open document flow', buildDocumentFlowPageUrl(documentType, entityId), {
          icon: 'document-flow',
          subtitle: 'Open workflow for this document',
          badge: 'Flow',
          keywords: ['flow', 'document flow', documentType],
        }),
        createRouteAction(`current:effects:${documentType}:${entityId}`, 'Open accounting effects', buildDocumentEffectsPageUrl(documentType, entityId), {
          icon: 'effects-flow',
          subtitle: 'Review ledger impact for this document',
          badge: 'Effects',
          keywords: ['effects', 'accounting effects', 'posting', documentType],
        }),
      )
    }
    return items
  }

  const documentReadonlyMatch = path.match(/^\/documents\/([^/]+)\/([^/]+)\/(effects|flow|print)$/)
  if (documentReadonlyMatch) {
    const [, documentType, entityId] = documentReadonlyMatch
    items.push(
      createRouteAction(`current:document:${documentType}:${entityId}`, 'Open source document', buildDocumentFullPageUrl(documentType, entityId), {
        icon: 'file-text',
        subtitle: 'Return to the source document',
        badge: 'Document',
        keywords: ['document', 'source document', documentType],
      }),
    )
    return items
  }

  if (/^\/documents\/[^/]+$/.test(path)) {
    const documentType = path.split('/')[2] ?? ''
    items.push(
      createRouteAction(`current:create:${documentType}`, 'Create new', buildDocumentFullPageUrl(documentType), {
        icon: 'plus',
        subtitle: 'Start a new record from this page',
        badge: 'Create',
        keywords: ['create', 'new', documentType],
      }),
    )
    return items
  }

  if (/^\/catalogs\/[^/]+$/.test(path)) {
    const catalogType = path.split('/')[2] ?? ''
    if (!excludedCatalogTypes.has(catalogType)) {
      items.push(
        createRouteAction(`current:create-catalog:${catalogType}`, 'Create new', buildCatalogFullPageUrl(catalogType), {
          icon: 'plus',
          subtitle: 'Start a new catalog record',
          badge: 'Create',
          keywords: ['create', 'new', catalogType],
        }),
      )
    }
    return items
  }

  if (path === '/accounting/general-journal-entries') {
    items.push(
      createRouteAction('current:create-gje', 'Create Journal Entry', buildGeneralJournalEntriesPath(), {
        icon: 'plus',
        subtitle: 'Start a new journal entry',
        badge: 'Create',
        keywords: ['create', 'general journal', 'journal entry', 'journal entries'],
      }),
    )
  }

  return items
}

export const NGB_ACCOUNTING_FAVORITE_ITEMS: CommandPaletteItemSeed[] = [
  {
    key: 'favorite:trial-balance',
    group: 'actions',
    kind: 'report',
    scope: 'reports',
    title: 'Trial Balance',
    subtitle: 'Review balances by account',
    icon: 'bar-chart',
    badge: 'Accounting',
    hint: null,
    route: buildReportPageUrl('accounting.trial_balance'),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['trial balance', 'accounting', 'report'],
    defaultRank: 0,
  },
  {
    key: 'favorite:period-closing',
    group: 'actions',
    kind: 'page',
    scope: 'pages',
    title: 'Period Close',
    subtitle: 'Manage the month-end close',
    icon: 'calendar-check',
    badge: 'Setup & Controls',
    hint: null,
    route: buildAccountingPeriodClosingPath(),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['period closing', 'close month'],
    defaultRank: 0,
  },
  {
    key: 'favorite:chart-of-accounts',
    group: 'actions',
    kind: 'page',
    scope: 'pages',
    title: 'Chart of Accounts',
    subtitle: 'Maintain the ledger account list',
    icon: 'book-open',
    badge: 'Setup & Controls',
    hint: null,
    route: buildChartOfAccountsPath(),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['chart of accounts', 'accounts', 'coa'],
    defaultRank: 0,
  },
]

export const NGB_ACCOUNTING_CREATE_ITEMS: CommandPaletteItemSeed[] = [
  createStaticCreateItem('create:gje', 'Create Journal Entry', buildGeneralJournalEntriesPath(), ['general journal entry', 'journal entry', 'journal entries']),
]

export const NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS: CommandPaletteItemSeed[] = [
  createStaticPageItem('page:chart-of-accounts', 'Chart of Accounts', buildChartOfAccountsPath(), 'book-open', ['chart of accounts', 'accounts', 'coa'], 'Setup & Controls'),
  createStaticPageItem('page:period-closing', 'Period Close', buildAccountingPeriodClosingPath(), 'calendar-check', ['period close', 'period closing', 'close month'], 'Setup & Controls'),
  createStaticPageItem('page:posting-log', 'Posting Log', buildReportPageUrl('accounting.posting_log'), 'history', ['posting log'], 'Setup & Controls'),
  createStaticPageItem('page:accounting-consistency', 'Integrity Checks', buildReportPageUrl('accounting.consistency'), 'shield-check', ['integrity checks', 'consistency', 'accounting consistency'], 'Setup & Controls'),
]
