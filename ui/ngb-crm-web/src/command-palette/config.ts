import { buildReportPageUrl, getReportDefinitions, searchCommandPalette, type CommandPaletteItemSeed, type CommandPaletteStoreConfig } from 'ngb-ui-framework'
import type { Router } from 'vue-router'

import {
  buildCRMHeuristicCurrentActions,
  resolveCRMReportPaletteIcon,
  CRM_CREATE_COMMAND_ITEMS,
  CRM_FAVORITE_ITEMS,
  CRM_SPECIAL_PAGE_ITEMS,
} from './crmStaticItems'

export function createCRMCommandPaletteConfig(router: Router): CommandPaletteStoreConfig {
  return {
    router,
    recentStorageKey: 'ngb:crm:command-palette:recent',
    buildHeuristicCurrentActions: buildCRMHeuristicCurrentActions,
    favoriteItems: CRM_FAVORITE_ITEMS,
    createItems: CRM_CREATE_COMMAND_ITEMS,
    specialPageItems: CRM_SPECIAL_PAGE_ITEMS,
    searchRemote: searchCommandPalette,
    loadReportItems: async (): Promise<CommandPaletteItemSeed[]> => {
      const definitions = await getReportDefinitions()
      return definitions
        .filter((definition) => definition.reportCode.startsWith('crm.'))
        .map((definition, index) => ({
          key: `report:${definition.reportCode}`,
          group: 'reports',
          kind: 'report',
          scope: 'reports',
          title: definition.name,
          subtitle: [definition.group, definition.description].filter((part) => String(part ?? '').trim().length > 0).join(' · ') || 'Run this report',
          icon: resolveCRMReportPaletteIcon({ reportCode: definition.reportCode, name: definition.name }),
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
