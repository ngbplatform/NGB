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

export type TradeStaticActionSeed = CommandPaletteItemSeed

const TRADE_HEURISTIC_OPTIONS: BuildNgbHeuristicCurrentActionsOptions = {
  excludedCatalogTypes: ['trd.accounting_policy'],
}

export function buildTradeHeuristicCurrentActions(fullRoute: string): CommandPaletteItemSeed[] {
  return buildNgbHeuristicCurrentActions(fullRoute, TRADE_HEURISTIC_OPTIONS)
}

export function resolveTradeReportPaletteIcon(input: { reportCode?: string | null; name?: string | null }): NgbIconName {
  const reportCode = String(input.reportCode ?? '').trim().toLowerCase()

  switch (reportCode) {
    case 'trd.dashboard_overview':
      return 'home'
    case 'trd.inventory_balances':
    case 'trd.inventory_movements':
    case 'trd.current_item_prices':
    case 'trd.sales_by_item':
    case 'trd.sales_by_customer':
    case 'trd.purchases_by_vendor':
      return 'bar-chart'
    case 'accounting.general_journal':
      return 'receipt'
    default:
      return 'bar-chart'
  }
}

export const TRADE_FAVORITE_ITEMS: TradeStaticActionSeed[] = [
  createStaticPageItem('page:home', 'Dashboard', '/home', 'home', ['trade dashboard', 'dashboard', 'home'], 'Overview'),
  {
    key: 'favorite:sales-invoices',
    group: 'actions',
    kind: 'page',
    scope: 'pages',
    title: 'Sales Invoices',
    subtitle: 'Review outbound sales documents',
    icon: 'file-text',
    badge: 'Sales',
    hint: null,
    route: '/documents/trd.sales_invoice',
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['sales invoices', 'sales', 'invoice'],
    defaultRank: 0,
  },
  {
    key: 'favorite:purchase-receipts',
    group: 'actions',
    kind: 'page',
    scope: 'pages',
    title: 'Purchase Receipts',
    subtitle: 'Track inbound stock receipts',
    icon: 'file-text',
    badge: 'Purchasing',
    hint: null,
    route: '/documents/trd.purchase_receipt',
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['purchase receipts', 'purchasing', 'receipt'],
    defaultRank: 0,
  },
  {
    key: 'favorite:inventory-balances',
    group: 'actions',
    kind: 'report',
    scope: 'reports',
    title: 'Inventory Balances',
    subtitle: 'See on-hand quantity by item and warehouse',
    icon: 'bar-chart',
    badge: 'Inventory',
    hint: null,
    route: buildReportPageUrl('trd.inventory_balances'),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['inventory balances', 'inventory', 'stock on hand'],
    defaultRank: 0,
  },
  {
    key: 'favorite:current-prices',
    group: 'actions',
    kind: 'report',
    scope: 'reports',
    title: 'Current Item Prices',
    subtitle: 'Review the active price book',
    icon: 'bar-chart',
    badge: 'Pricing',
    hint: null,
    route: buildReportPageUrl('trd.current_item_prices'),
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['prices', 'price book', 'item prices'],
    defaultRank: 0,
  },
  ...NGB_ACCOUNTING_FAVORITE_ITEMS,
]

export const TRADE_CREATE_COMMAND_ITEMS: TradeStaticActionSeed[] = [
  createStaticCreateItem('create:purchase-receipt', 'Create Purchase Receipt', buildDocumentFullPageUrl('trd.purchase_receipt'), ['purchase receipt', 'purchasing']),
  createStaticCreateItem('create:sales-invoice', 'Create Sales Invoice', buildDocumentFullPageUrl('trd.sales_invoice'), ['sales invoice', 'sale']),
  createStaticCreateItem('create:customer-payment', 'Create Customer Payment', buildDocumentFullPageUrl('trd.customer_payment'), ['customer payment', 'payment']),
  createStaticCreateItem('create:vendor-payment', 'Create Vendor Payment', buildDocumentFullPageUrl('trd.vendor_payment'), ['vendor payment', 'payment']),
  createStaticCreateItem('create:inventory-transfer', 'Create Inventory Transfer', buildDocumentFullPageUrl('trd.inventory_transfer'), ['inventory transfer', 'transfer']),
  createStaticCreateItem('create:inventory-adjustment', 'Create Inventory Adjustment', buildDocumentFullPageUrl('trd.inventory_adjustment'), ['inventory adjustment', 'adjustment']),
  createStaticCreateItem('create:item-price-update', 'Create Item Price Update', buildDocumentFullPageUrl('trd.item_price_update'), ['item price update', 'pricing']),
  ...NGB_ACCOUNTING_CREATE_ITEMS,
]

export const TRADE_SPECIAL_PAGE_ITEMS: TradeStaticActionSeed[] = [
  ...NGB_ACCOUNTING_SPECIAL_PAGE_ITEMS,
  createStaticPageItem('page:dashboard', 'Dashboard', '/home', 'home', ['home', 'dashboard', 'trade'], 'Overview'),
  createStaticPageItem('page:accounting-policy', 'Accounting Policy', '/catalogs/trd.accounting_policy', 'settings', ['accounting policy', 'trade policy'], 'Setup & Controls'),
]

function createStaticCreateItem(key: string, title: string, route: string, keywords: string[]): TradeStaticActionSeed {
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
): TradeStaticActionSeed {
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
