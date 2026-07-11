import {
  buildDocumentFullPageUrl,
  buildNgbHeuristicCurrentActions,
  buildReportPageUrl,
  type BuildNgbHeuristicCurrentActionsOptions,
  type CommandPaletteGroupCode,
  type CommandPaletteItemSeed,
  type CommandPaletteScope,
  type NgbIconName,
} from '@ngbplatform/ui'

export type CRMStaticActionSeed = CommandPaletteItemSeed

const CRM_HEURISTIC_OPTIONS: BuildNgbHeuristicCurrentActionsOptions = {}

export function buildCRMHeuristicCurrentActions(fullRoute: string): CommandPaletteItemSeed[] {
  return buildNgbHeuristicCurrentActions(fullRoute, CRM_HEURISTIC_OPTIONS)
}

export function resolveCRMReportPaletteIcon(input: { reportCode?: string | null }): NgbIconName {
  const reportCode = String(input.reportCode ?? '').trim().toLowerCase()

  switch (reportCode) {
    case 'crm.sales_pipeline':
      return 'bar-chart'
    case 'crm.lead_conversion_funnel':
      return 'filter'
    case 'crm.activity_summary':
      return 'calendar-check'
    case 'crm.quote_register':
      return 'file-text'
    case 'crm.opportunity_history':
      return 'history'
    default:
      return 'bar-chart'
  }
}

export const CRM_FAVORITE_ITEMS: CRMStaticActionSeed[] = [
  createStaticPageItem('page:home', 'Dashboard', '/home', 'home', ['crm dashboard', 'dashboard', 'home'], 'Overview'),
  createStaticPageItem('favorite:accounts', 'Accounts', '/catalogs/crm.account', 'building-2', ['accounts', 'customers', 'companies'], 'Customers'),
  createStaticPageItem('favorite:opportunities', 'Opportunities', '/documents/crm.lead_conversion', 'file-text', ['opportunities', 'pipeline', 'deals'], 'Pipeline'),
  createStaticPageItem('favorite:sales-pipeline', 'Sales Pipeline', buildReportPageUrl('crm.sales_pipeline'), 'bar-chart', ['sales pipeline', 'pipeline', 'forecast'], 'Reports'),
  createStaticPageItem('favorite:quote-register', 'Quote Register', buildReportPageUrl('crm.quote_register'), 'file-text', ['quotes', 'quote register'], 'Reports'),
]

export const CRM_CREATE_COMMAND_ITEMS: CRMStaticActionSeed[] = [
  createStaticCreateItem('create:lead-intake', 'Create Lead Intake', buildDocumentFullPageUrl('crm.lead_intake'), ['lead', 'intake']),
  createStaticCreateItem('create:lead-qualification', 'Create Lead Qualification', buildDocumentFullPageUrl('crm.lead_qualification'), ['qualification', 'score']),
  createStaticCreateItem('create:lead-conversion', 'Create Lead Conversion', buildDocumentFullPageUrl('crm.lead_conversion'), ['conversion', 'opportunity']),
  createStaticCreateItem('create:opportunity-update', 'Create Opportunity Update', buildDocumentFullPageUrl('crm.opportunity_update'), ['opportunity', 'stage']),
  createStaticCreateItem('create:quote', 'Create Quote', buildDocumentFullPageUrl('crm.quote'), ['quote', 'proposal']),
  createStaticCreateItem('create:activity', 'Create Activity Log', buildDocumentFullPageUrl('crm.activity_log'), ['activity', 'call', 'meeting']),
]

export const CRM_SPECIAL_PAGE_ITEMS: CRMStaticActionSeed[] = [
  createStaticPageItem('page:dashboard', 'Dashboard', '/home', 'home', ['home', 'dashboard', 'crm'], 'Overview'),
  createStaticPageItem('page:contacts', 'Contacts', '/catalogs/crm.contact', 'user', ['contacts', 'people'], 'Customers'),
  createStaticPageItem('page:products', 'Products', '/catalogs/crm.product', 'tag', ['products', 'price'], 'Quotes'),
  createStaticPageItem('page:stages', 'Opportunity Stages', '/catalogs/crm.opportunity_stage', 'list', ['stages', 'pipeline setup'], 'Setup'),
]

function createStaticCreateItem(key: string, title: string, route: string, keywords: string[]): CRMStaticActionSeed {
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
): CRMStaticActionSeed {
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
