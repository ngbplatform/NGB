import { buildReportPageUrl, getReportDefinitions, searchCommandPalette, type CommandPaletteItemSeed, type CommandPaletteStoreConfig } from 'ngb-ui-framework'
import type { Router } from 'vue-router'

import {
  buildTradeHeuristicCurrentActions,
  resolveTradeReportPaletteIcon,
  TRADE_CREATE_COMMAND_ITEMS,
  TRADE_FAVORITE_ITEMS,
  TRADE_SPECIAL_PAGE_ITEMS,
} from './tradeStaticItems'

export function createTradeCommandPaletteConfig(router: Router): CommandPaletteStoreConfig {
  return {
    router,
    recentStorageKey: 'ngb:trade:command-palette:recent',
    buildHeuristicCurrentActions: buildTradeHeuristicCurrentActions,
    favoriteItems: TRADE_FAVORITE_ITEMS,
    createItems: TRADE_CREATE_COMMAND_ITEMS,
    specialPageItems: TRADE_SPECIAL_PAGE_ITEMS,
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
          icon: resolveTradeReportPaletteIcon({ reportCode: definition.reportCode, name: definition.name }),
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
