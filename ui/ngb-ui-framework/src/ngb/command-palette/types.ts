import type { Router } from 'vue-router'

export type CommandPaletteScope = 'commands' | 'pages' | 'reports' | 'documents' | 'catalogs'

export type CommandPaletteGroupCode = 'actions' | 'go-to' | 'documents' | 'catalogs' | 'reports' | 'recent'

export type CommandPaletteItemKind = 'page' | 'document' | 'catalog' | 'report' | 'command' | 'recent'

export type CommandPaletteExecutionMode = 'default' | 'new-tab'

export type CommandPaletteAction = () => void | Promise<void>

export type CommandPaletteItem = {
  key: string
  group: CommandPaletteGroupCode
  kind: CommandPaletteItemKind
  scope: CommandPaletteScope
  title: string
  subtitle?: string | null
  icon?: string | null
  badge?: string | null
  hint?: string | null
  route?: string | null
  commandCode?: string | null
  status?: string | null
  openInNewTabSupported?: boolean
  keywords?: string[]
  perform?: CommandPaletteAction
  defaultRank: number
  score: number
  isCurrentContext?: boolean
  isFavorite?: boolean
  isRecent?: boolean
  source: 'local' | 'remote'
}

export type CommandPaletteItemSeed = Omit<CommandPaletteItem, 'score' | 'source'>

export type CommandPaletteGroup = {
  code: CommandPaletteGroupCode
  label: string
  items: CommandPaletteItem[]
}

export type CommandPaletteExplicitContext = {
  entityType?: 'document' | 'catalog' | 'report' | 'page'
  documentType?: string | null
  catalogType?: string | null
  entityId?: string | null
  title?: string | null
  actions: CommandPaletteItemSeed[]
}

export type CommandPaletteRecentEntry = {
  key: string
  kind: CommandPaletteItemKind
  scope: CommandPaletteScope
  title: string
  subtitle?: string | null
  icon?: string | null
  badge?: string | null
  route?: string | null
  status?: string | null
  openInNewTabSupported?: boolean
  timestamp: string
}

export type CommandPaletteSearchContextDto = {
  entityType?: string | null
  documentType?: string | null
  catalogType?: string | null
  entityId?: string | null
}

export type CommandPaletteSearchRequestDto = {
  query: string
  scope?: CommandPaletteScope | null
  limit?: number
  currentRoute?: string | null
  context?: CommandPaletteSearchContextDto | null
}

export type CommandPaletteResultItemDto = {
  key: string
  kind: CommandPaletteItemKind | 'page'
  title: string
  subtitle?: string | null
  icon?: string | null
  badge?: string | null
  route?: string | null
  commandCode?: string | null
  status?: string | null
  openInNewTabSupported: boolean
  score: number
}

export type CommandPaletteGroupDto = {
  code: 'documents' | 'catalogs' | 'reports' | 'go-to' | 'actions' | 'recent'
  label: string
  items: CommandPaletteResultItemDto[]
}

export type CommandPaletteSearchResponseDto = {
  groups: CommandPaletteGroupDto[]
}

export type CommandPaletteStoreConfig = {
  router: Router
  recentStorageKey: string
  searchRemote?: (
    request: CommandPaletteSearchRequestDto,
    signal?: AbortSignal,
  ) => Promise<CommandPaletteSearchResponseDto>
  loadReportItems?: () => Promise<CommandPaletteItemSeed[]>
  buildHeuristicCurrentActions?: (fullRoute: string) => CommandPaletteItemSeed[]
  favoriteItems?: CommandPaletteItemSeed[]
  createItems?: CommandPaletteItemSeed[]
  specialPageItems?: CommandPaletteItemSeed[]
}

