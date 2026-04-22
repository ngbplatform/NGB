import {
  buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions,
  buildReportPageUrl,
  NGB_ACCOUNTING_CREATE_ITEMS,
  NGB_ACCOUNTING_FAVORITE_ITEMS,
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
  type BuildNgbHeuristicCurrentActionsOptions,
  type CommandPaletteGroupCode,
  type CommandPaletteItemSeed,
  type CommandPaletteScope,
  type NgbIconName,
} from 'ngb-ui-framework'

export type PmStaticActionSeed = CommandPaletteItemSeed

type StaticActionOptions = Partial<PmStaticActionSeed>

const PM_HEURISTIC_OPTIONS: BuildNgbHeuristicCurrentActionsOptions = {
  excludedCatalogTypes: ['pm.accounting_policy', 'pm.property'],
}

export function buildPmHeuristicCurrentActions(fullRoute: string): CommandPaletteItemSeed[] {
  const url = new URL(fullRoute || '/', 'https://ngb.local')
  const items = buildNgbHeuristicCurrentActions(fullRoute, PM_HEURISTIC_OPTIONS)

  if (url.pathname === '/catalogs/pm.property') {
    items.push(
      createRouteAction('current:create-building', 'Create new building', '/catalogs/pm.property?panel=new&newKind=Building', {
        icon: 'plus',
        subtitle: 'Start a new building record',
        badge: 'Create',
        keywords: ['create', 'building', 'property'],
      }),
    )
  }

  return items
}

export function resolvePmReportPaletteIcon(input: { reportCode?: string | null; name?: string | null }): NgbIconName {
  const reportCode = String(input.reportCode ?? '').trim().toLowerCase()

  switch (reportCode) {
    case 'pm.tenant.statement':
    case 'pm.receivables.open_items.details':
      return 'file-text'
    case 'pm.maintenance.queue':
    case 'pm.receivables.open_items':
      return 'list'
    case 'accounting.general_journal':
      return 'receipt'
    case 'accounting.account_card':
    case 'accounting.general_ledger_aggregated':
      return 'book-open'
    default:
      return 'bar-chart'
  }
}

export const PM_FAVORITE_ITEMS: PmStaticActionSeed[] = [
  {
    key: 'favorite:receivables-open-items',
    group: 'actions',
    kind: 'page',
    scope: 'pages',
    title: 'Open Items',
    subtitle: 'Review open receivable balances',
    icon: 'list',
    badge: 'Receivables',
    hint: null,
    route: '/receivables/open-items',
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['receivables', 'open items', 'ar'],
    defaultRank: 0,
  },
  ...NGB_ACCOUNTING_FAVORITE_ITEMS,
  {
    key: 'favorite:maintenance-queue',
    group: 'actions',
    kind: 'report',
    scope: 'reports',
    title: 'Open Queue',
    subtitle: 'Track open maintenance requests',
    icon: 'list',
    badge: 'Maintenance',
    hint: null,
    route: buildReportPageUrl('pm.maintenance.queue'),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['maintenance queue', 'maintenance', 'queue'],
    defaultRank: 0,
  },
]

export const PM_CREATE_COMMAND_ITEMS: PmStaticActionSeed[] = [
  createStaticCreateItem('create:lease', 'Create Lease', buildDocumentFullPageUrl('pm.lease'), ['lease']),
  createStaticCreateItem('create:receivable-charge', 'Create Receivable Charge', buildDocumentFullPageUrl('pm.receivable_charge'), ['receivable charge']),
  createStaticCreateItem('create:rent-charge', 'Create Rent Charge', buildDocumentFullPageUrl('pm.rent_charge'), ['rent charge']),
  createStaticCreateItem('create:late-fee-charge', 'Create Late Fee Charge', buildDocumentFullPageUrl('pm.late_fee_charge'), ['late fee charge']),
  createStaticCreateItem('create:receivable-payment', 'Create Receivable Payment', buildDocumentFullPageUrl('pm.receivable_payment'), ['receivable payment']),
  createStaticCreateItem('create:receivable-returned-payment', 'Create Receivable Returned Payment', buildDocumentFullPageUrl('pm.receivable_returned_payment'), ['returned payment']),
  createStaticCreateItem('create:receivable-credit-memo', 'Create Receivable Credit Memo', buildDocumentFullPageUrl('pm.receivable_credit_memo'), ['receivable credit memo', 'credit memo']),
  createStaticCreateItem('create:payable-charge', 'Create Payable Charge', buildDocumentFullPageUrl('pm.payable_charge'), ['payable charge']),
  createStaticCreateItem('create:payable-payment', 'Create Payable Payment', buildDocumentFullPageUrl('pm.payable_payment'), ['payable payment']),
  createStaticCreateItem('create:payable-credit-memo', 'Create Payable Credit Memo', buildDocumentFullPageUrl('pm.payable_credit_memo'), ['payable credit memo']),
  ...NGB_ACCOUNTING_CREATE_ITEMS,
]

export const PM_SPECIAL_PAGE_ITEMS: PmStaticActionSeed[] = [
  ...NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
  createStaticPageItem('page:accounting-policy', 'Accounting Policy', '/catalogs/pm.accounting_policy', 'settings', ['accounting policy'], 'Setup & Controls'),
]

function createRouteAction(
  key: string,
  title: string,
  route: string,
  options?: StaticActionOptions,
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

function createStaticCreateItem(key: string, title: string, route: string, keywords: string[]): PmStaticActionSeed {
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
): PmStaticActionSeed {
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
