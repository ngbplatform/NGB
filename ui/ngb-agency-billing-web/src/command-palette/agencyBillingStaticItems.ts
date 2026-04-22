import {
  buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions,
  NGB_ACCOUNTING_CREATE_ITEMS,
  NGB_ACCOUNTING_FAVORITE_ITEMS,
  NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
  type BuildNgbHeuristicCurrentActionsOptions,
  type CommandPaletteGroupCode,
  type CommandPaletteItemSeed,
  type CommandPaletteScope,
  type NgbIconName,
} from 'ngb-ui-framework'

export type AgencyBillingStaticActionSeed = CommandPaletteItemSeed

const AGENCY_BILLING_HEURISTIC_OPTIONS: BuildNgbHeuristicCurrentActionsOptions = {
  excludedCatalogTypes: ['ab.accounting_policy'],
}

export function buildAgencyBillingHeuristicCurrentActions(fullRoute: string): CommandPaletteItemSeed[] {
  return buildNgbHeuristicCurrentActions(fullRoute, AGENCY_BILLING_HEURISTIC_OPTIONS)
}

export function resolveAgencyBillingReportPaletteIcon(input: { reportCode?: string | null; name?: string | null }): NgbIconName {
  const reportCode = String(input.reportCode ?? '').trim().toLowerCase()

  switch (reportCode) {
    case 'ab.dashboard_overview':
      return 'home'
    case 'ab.unbilled_time_report':
      return 'calendar-check'
    case 'ab.invoice_register':
      return 'receipt'
    case 'ab.ar_aging':
      return 'book-open'
    case 'ab.team_utilization':
      return 'users'
    case 'ab.project_profitability':
      return 'bar-chart'
    default:
      return 'bar-chart'
  }
}

export const AGENCY_BILLING_FAVORITE_ITEMS: AgencyBillingStaticActionSeed[] = [
  createStaticPageItem('page:home', 'Dashboard', '/home', 'home', ['agency billing dashboard', 'dashboard', 'home'], 'Operational overview'),
  createStaticPageItem('page:timesheets', 'Timesheets', '/documents/ab.timesheet', 'calendar-check', ['timesheets', 'time capture', 'hours'], 'Capture and review billable time'),
  createStaticPageItem('page:sales-invoices', 'Sales Invoices', '/documents/ab.sales_invoice', 'receipt', ['sales invoices', 'invoices', 'billing'], 'Prepare and track invoices'),
  createStaticPageItem('page:customer-payments', 'Customer Payments', '/documents/ab.customer_payment', 'wallet', ['payments', 'receipts', 'collections'], 'Record incoming cash'),
  ...NGB_ACCOUNTING_FAVORITE_ITEMS,
]

export const AGENCY_BILLING_CREATE_COMMAND_ITEMS: AgencyBillingStaticActionSeed[] = [
  createStaticCreateItem('create:client-contract', 'Create Client Contract', buildDocumentFullPageUrl('ab.client_contract'), ['client contract', 'agreement']),
  createStaticCreateItem('create:timesheet', 'Create Timesheet', buildDocumentFullPageUrl('ab.timesheet'), ['timesheet', 'time entry', 'hours']),
  createStaticCreateItem('create:sales-invoice', 'Create Sales Invoice', buildDocumentFullPageUrl('ab.sales_invoice'), ['sales invoice', 'invoice', 'billing']),
  createStaticCreateItem('create:customer-payment', 'Create Customer Payment', buildDocumentFullPageUrl('ab.customer_payment'), ['customer payment', 'payment', 'cash receipt']),
  ...NGB_ACCOUNTING_CREATE_ITEMS,
]

export const AGENCY_BILLING_SPECIAL_PAGE_ITEMS: AgencyBillingStaticActionSeed[] = [
  ...NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
  createStaticPageItem('page:dashboard', 'Dashboard', '/home', 'home', ['home', 'dashboard', 'agency billing'], 'Overview'),
  createStaticPageItem('page:team-members', 'Team Members', '/catalogs/ab.team_member', 'users', ['team members', 'resources', 'staff'], 'Portfolio'),
  createStaticPageItem('page:projects', 'Projects', '/catalogs/ab.project', 'clipboard-list', ['projects', 'portfolio', 'engagements'], 'Portfolio'),
  createStaticPageItem('page:rate-cards', 'Rate Cards', '/catalogs/ab.rate_card', 'calculator', ['rate cards', 'rates', 'pricing'], 'Portfolio'),
  createStaticPageItem('page:accounting-policy', 'Accounting Policy', '/catalogs/ab.accounting_policy', 'settings', ['accounting policy', 'agency billing policy'], 'Setup & controls'),
]

function createStaticCreateItem(key: string, title: string, route: string, keywords: string[]): AgencyBillingStaticActionSeed {
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
): AgencyBillingStaticActionSeed {
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
