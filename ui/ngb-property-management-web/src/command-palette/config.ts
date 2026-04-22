import { buildReportPageUrl, getReportDefinitions, searchCommandPalette, type CommandPaletteItemSeed, type CommandPaletteStoreConfig } from 'ngb-ui-framework'
import type { Router } from 'vue-router'
import {
  buildPmHeuristicCurrentActions,
  PM_CREATE_COMMAND_ITEMS,
  PM_FAVORITE_ITEMS,
  PM_SPECIAL_PAGE_ITEMS,
  resolvePmReportPaletteIcon,
} from './pmStaticItems'

export function createPmCommandPaletteConfig(router: Router): CommandPaletteStoreConfig {
  return {
    router,
    recentStorageKey: 'ngb:pm:command-palette:recent',
    buildHeuristicCurrentActions: buildPmHeuristicCurrentActions,
    favoriteItems: PM_FAVORITE_ITEMS,
    createItems: PM_CREATE_COMMAND_ITEMS,
    specialPageItems: PM_SPECIAL_PAGE_ITEMS,
    searchRemote: searchCommandPalette,
    loadReportItems: async (): Promise<CommandPaletteItemSeed[]> => {
      const definitions = await getReportDefinitions()
      return definitions
        .filter((definition) => definition.reportCode !== 'accounting.posting_log' && definition.reportCode !== 'accounting.consistency')
        .map((definition, index) => ({
          key: `report:${definition.reportCode}`,
          group: 'reports',
          kind: 'report',
          scope: 'reports',
          title: definition.name,
          subtitle: [definition.group, definition.description].filter((part) => String(part ?? '').trim().length > 0).join(' · ') || 'Run this report',
          icon: resolvePmReportPaletteIcon({ reportCode: definition.reportCode, name: definition.name }),
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
