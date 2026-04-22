import {
  getReportDefinitions,
  searchCommandPalette,
  type CommandPaletteItemSeed,
  type CommandPaletteStoreConfig,
  buildReportPageUrl,
} from 'ngb-ui-framework'
import type { Router } from 'vue-router'

import {
  AGENCY_BILLING_CREATE_COMMAND_ITEMS,
  AGENCY_BILLING_FAVORITE_ITEMS,
  AGENCY_BILLING_SPECIAL_PAGE_ITEMS,
  buildAgencyBillingHeuristicCurrentActions,
  resolveAgencyBillingReportPaletteIcon,
} from './agencyBillingStaticItems'

export function createAgencyBillingCommandPaletteConfig(router: Router): CommandPaletteStoreConfig {
  return {
    router,
    recentStorageKey: 'ngb:agency-billing:command-palette:recent',
    buildHeuristicCurrentActions: buildAgencyBillingHeuristicCurrentActions,
    favoriteItems: AGENCY_BILLING_FAVORITE_ITEMS,
    createItems: AGENCY_BILLING_CREATE_COMMAND_ITEMS,
    specialPageItems: AGENCY_BILLING_SPECIAL_PAGE_ITEMS,
    searchRemote: searchCommandPalette,
    loadReportItems: async (): Promise<CommandPaletteItemSeed[]> => {
      const definitions = await getReportDefinitions()
      return definitions
        .filter((definition) => definition.reportCode.startsWith('ab.') && definition.reportCode !== 'accounting.posting_log' && definition.reportCode !== 'accounting.consistency')
        .map((definition, index) => ({
          key: `report:${definition.reportCode}`,
          group: 'reports',
          kind: 'report',
          scope: 'reports',
          title: definition.name,
          subtitle: [definition.group, definition.description].filter((part) => String(part ?? '').trim().length > 0).join(' · ') || 'Run this report',
          icon: resolveAgencyBillingReportPaletteIcon({ reportCode: definition.reportCode, name: definition.name }),
          badge: 'Report',
          hint: null,
          route: buildReportPageUrl(definition.reportCode),
          commandCode: null,
          status: null,
          openInNewTabSupported: true,
          keywords: [definition.reportCode, definition.group ?? '', definition.description ?? ''],
          defaultRank: 700 - index,
        }))
    },
  }
}
