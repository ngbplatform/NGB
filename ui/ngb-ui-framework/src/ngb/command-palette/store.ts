import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'
import { isNgbIconName, type NgbIconName } from '../primitives/iconNames'
import { useMainMenuStore, type MainMenuGroup } from '../site/mainMenuStore'
import { getConfiguredNgbCommandPalette } from './config'
import { loadCommandPaletteRecent, saveCommandPaletteRecent } from './storage'
import { defaultSearchFields, groupOrder, parseCommandPaletteQuery, scoreSearchText } from './search'
import type {
  CommandPaletteExecutionMode,
  CommandPaletteExplicitContext,
  CommandPaletteGroup,
  CommandPaletteGroupCode,
  CommandPaletteItem,
  CommandPaletteItemSeed,
  CommandPaletteRecentEntry,
  CommandPaletteScope,
  CommandPaletteSearchContextDto,
} from './types'

const REMOTE_DEBOUNCE_MS = 150
const REMOTE_LIMIT = 20
const MAX_RECENT = 8

export const useCommandPaletteStore = defineStore('commandPalette', () => {
  const config = getConfiguredNgbCommandPalette()
  const menuStore = useMainMenuStore()

  const isOpen = ref(false)
  const query = ref('')
  const activeIndex = ref(0)
  const currentRoute = ref('/')
  const focusRequestKey = ref(0)
  const recentEntries = ref<CommandPaletteRecentEntry[]>([])
  const reportItems = ref<CommandPaletteItemSeed[]>([])
  const reportsLoaded = ref(false)
  const reportsLoading = ref(false)
  const remoteGroups = ref<CommandPaletteGroup[]>([])
  const remoteLoading = ref(false)
  const remoteError = ref<string | null>(null)
  const explicitContextOwnerId = ref<string | null>(null)
  const explicitContext = ref<CommandPaletteExplicitContext | null>(null)
  const isHydrated = ref(false)

  let remoteTimer: ReturnType<typeof setTimeout> | null = null
  let remoteAbortController: AbortController | null = null
  let remoteSequence = 0

  const parsedQuery = computed(() => parseCommandPaletteQuery(query.value))
  const cleanQuery = computed(() => parsedQuery.value.query)
  const activeScope = computed<CommandPaletteScope | null>(() => parsedQuery.value.scope)

  const localGroups = computed<CommandPaletteGroup[]>(() => {
    const results: CommandPaletteGroup[] = []
    const actions = limitGroupItems(
      'actions',
      dedupeItemsByKey([
        ...scoreItems(buildCurrentActionItems(), cleanQuery.value, activeScope.value),
        ...scoreItems(buildFavoriteItems(), cleanQuery.value, activeScope.value),
        ...scoreItems(buildCreateCommandItems(), cleanQuery.value, activeScope.value),
      ]),
      cleanQuery.value,
    )

    const goTo = limitGroupItems(
      'go-to',
      scoreItems(buildGoToItems(menuStore.groups), cleanQuery.value, activeScope.value),
      cleanQuery.value,
    )

    const reports = limitGroupItems(
      'reports',
      scoreItems(buildReportItems(), cleanQuery.value, activeScope.value),
      cleanQuery.value,
    )

    const recent = limitGroupItems(
      'recent',
      scoreItems(buildRecentItems(), cleanQuery.value, activeScope.value),
      cleanQuery.value,
    )

    if (actions.length > 0) results.push({ code: 'actions', label: 'Actions', items: actions })
    if (goTo.length > 0) results.push({ code: 'go-to', label: 'Go to', items: goTo })
    if (reports.length > 0) results.push({ code: 'reports', label: 'Reports', items: reports })
    if (recent.length > 0) results.push({ code: 'recent', label: 'Recent', items: recent })

    return results
  })

  const groups = computed<CommandPaletteGroup[]>(() => {
    const merged = new Map<CommandPaletteGroupCode, CommandPaletteItem[]>()

    for (const group of [...localGroups.value, ...remoteGroups.value]) {
      const existing = merged.get(group.code) ?? []
      const next = dedupeItemsByKey([...existing, ...group.items])
        .sort((a, b) => b.score - a.score || a.title.localeCompare(b.title))
      merged.set(group.code, next)
    }

    return Array.from(merged.entries())
      .sort((a, b) => groupOrder(a[0]) - groupOrder(b[0]))
      .map(([code, items]) => ({
        code,
        label: resolveGroupLabel(code),
        items: items.slice(0, visibleLimitForGroup(code, cleanQuery.value)),
      }))
      .filter((group) => group.items.length > 0)
  })

  const flatItems = computed(() => groups.value.flatMap((group) => group.items))

  const hasResults = computed(() => flatItems.value.length > 0)
  const hasRemoteError = computed(() => !!remoteError.value)
  const showRemoteLoading = computed(() =>
    remoteLoading.value
    && cleanQuery.value.length >= 2
    && activeScope.value !== 'commands'
    && activeScope.value !== 'pages')

  watch(flatItems, (items) => {
    if (items.length === 0) {
      activeIndex.value = 0
      return
    }

    activeIndex.value = Math.max(0, Math.min(activeIndex.value, items.length - 1))
  })

  watch(
    () => [
      isOpen.value,
      cleanQuery.value,
      activeScope.value,
      currentRoute.value,
      explicitContext.value?.documentType,
      explicitContext.value?.catalogType,
      explicitContext.value?.entityId,
    ] as const,
    () => {
      scheduleRemoteSearch()
    },
  )

  async function hydrate(): Promise<void> {
    if (isHydrated.value) return

    recentEntries.value = loadCommandPaletteRecent(config.recentStorageKey)
    currentRoute.value = config.router.currentRoute.value.fullPath || '/'
    isHydrated.value = true
  }

  async function ensureReportItems(): Promise<void> {
    if (reportsLoaded.value || reportsLoading.value || !config.loadReportItems) return

    reportsLoading.value = true
    try {
      reportItems.value = await config.loadReportItems()
      reportsLoaded.value = true
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error(error)
    } finally {
      reportsLoading.value = false
    }
  }

  function open(): void {
    void hydrate()
    void ensureReportItems()
    currentRoute.value = config.router.currentRoute.value.fullPath || currentRoute.value
    isOpen.value = true
    focusRequestKey.value += 1
    if (!query.value) activeIndex.value = 0
  }

  function close(): void {
    isOpen.value = false
    query.value = ''
    activeIndex.value = 0
    remoteError.value = null
    remoteLoading.value = false
    remoteGroups.value = []
    clearScheduledRemoteSearch()
  }

  function setQuery(nextValue: string): void {
    query.value = nextValue
    activeIndex.value = 0
  }

  function setCurrentRoute(route: string): void {
    currentRoute.value = route || '/'
  }

  function setExplicitContext(ownerId: string, context: CommandPaletteExplicitContext | null): void {
    explicitContextOwnerId.value = ownerId
    explicitContext.value = context
  }

  function clearExplicitContext(ownerId: string): void {
    if (explicitContextOwnerId.value !== ownerId) return
    explicitContextOwnerId.value = null
    explicitContext.value = null
  }

  function moveActive(delta: number): void {
    if (flatItems.value.length === 0) return
    activeIndex.value = Math.max(0, Math.min(activeIndex.value + delta, flatItems.value.length - 1))
  }

  function setActiveIndex(index: number): void {
    if (flatItems.value.length === 0) {
      activeIndex.value = 0
      return
    }

    activeIndex.value = Math.max(0, Math.min(index, flatItems.value.length - 1))
  }

  async function executeActive(mode: CommandPaletteExecutionMode = 'default'): Promise<void> {
    const item = flatItems.value[activeIndex.value]
    if (!item) return
    await executeItem(item, mode)
  }

  async function executeItem(item: CommandPaletteItem, mode: CommandPaletteExecutionMode = 'default'): Promise<void> {
    close()

    try {
      if (mode === 'new-tab' && item.route && item.openInNewTabSupported) {
        openRouteInNewTab(item.route)
        recordRecent(item)
        return
      }

      if (item.perform) {
        await item.perform()
      } else if (item.route) {
        await config.router.push(item.route)
      }

      recordRecent(item)
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error(error)
    }
  }

  function recordRecent(item: CommandPaletteItem): void {
    if (!isRecentTrackable(item)) return

    const nextEntry: CommandPaletteRecentEntry = {
      key: item.key,
      kind: item.kind,
      scope: item.scope,
      title: item.title,
      subtitle: item.subtitle ?? null,
      icon: item.icon ?? null,
      badge: item.badge ?? null,
      route: item.route ?? null,
      status: item.status ?? null,
      openInNewTabSupported: Boolean(item.openInNewTabSupported),
      timestamp: new Date().toISOString(),
    }

    recentEntries.value = [nextEntry, ...recentEntries.value.filter((entry) => entry.key !== nextEntry.key)].slice(0, MAX_RECENT)
    saveCommandPaletteRecent(config.recentStorageKey, recentEntries.value)
  }

  function clearScheduledRemoteSearch(): void {
    if (remoteTimer) {
      clearTimeout(remoteTimer)
      remoteTimer = null
    }

    remoteAbortController?.abort()
    remoteAbortController = null
  }

  function scheduleRemoteSearch(): void {
    clearScheduledRemoteSearch()

    if (!isOpen.value || !shouldRunRemoteSearch(cleanQuery.value, activeScope.value, Boolean(config.searchRemote))) {
      remoteLoading.value = false
      remoteError.value = null
      remoteGroups.value = []
      return
    }

    remoteLoading.value = true
    remoteError.value = null
    const seq = ++remoteSequence

    remoteTimer = setTimeout(async () => {
      remoteAbortController = new AbortController()
      try {
        const response = await config.searchRemote!({
          query: cleanQuery.value,
          scope: activeScope.value ?? null,
          limit: REMOTE_LIMIT,
          currentRoute: currentRoute.value,
          context: buildSearchContext(explicitContext.value),
        }, remoteAbortController.signal)

        if (seq !== remoteSequence) return

        remoteGroups.value = response.groups
          .filter((group) => group.code !== 'reports')
          .map((group) => ({
            code: group.code as CommandPaletteGroupCode,
            label: group.label,
            items: group.items.map((item) => ({
              key: item.key,
              group: group.code as CommandPaletteGroupCode,
              kind: item.kind as CommandPaletteItem['kind'],
              scope: scopeForRemoteKind(item.kind),
              title: item.title,
              subtitle: item.subtitle ?? null,
              icon: resolveIconName(item.icon, item.kind === 'catalog' ? 'grid' : 'file-text'),
              badge: item.badge ?? null,
              hint: null,
              route: item.route ?? null,
              commandCode: item.commandCode ?? null,
              status: item.status ?? null,
              openInNewTabSupported: item.openInNewTabSupported,
              keywords: [],
              defaultRank: 0,
              score: Number(item.score ?? 0),
              source: 'remote',
            })),
          }))
      } catch (error) {
        if (remoteAbortController?.signal.aborted) return
        if (seq !== remoteSequence) return

        remoteGroups.value = []
        remoteError.value = error instanceof Error && error.message.trim()
          ? error.message
          : 'Could not update remote results.'
      } finally {
        if (seq === remoteSequence) remoteLoading.value = false
      }
    }, REMOTE_DEBOUNCE_MS)
  }

  function buildCurrentActionItems(): CommandPaletteItem[] {
    const explicit = explicitContext.value?.actions ?? []
    const heuristic = config.buildHeuristicCurrentActions?.(currentRoute.value) ?? []
    return materializeLocalItems(dedupeSeedsByKey([...explicit, ...heuristic]), {
      source: 'local',
      score: 0,
      isCurrentContext: true,
      iconFallback: 'search',
      rankFrom: 1_000,
    })
  }

  function buildFavoriteItems(): CommandPaletteItem[] {
    return materializeLocalItems(config.favoriteItems ?? [], {
      source: 'local',
      score: 0,
      isFavorite: true,
      rankFrom: 820,
      iconFallback: 'file-text',
    })
  }

  function buildCreateCommandItems(): CommandPaletteItem[] {
    return materializeLocalItems(config.createItems ?? [], {
      source: 'local',
      score: 0,
      rankFrom: 780,
      iconFallback: 'plus',
    })
  }

  function buildReportItems(): CommandPaletteItem[] {
    return materializeLocalItems(reportItems.value, {
      source: 'local',
      score: 0,
      rankFrom: 700,
      iconFallback: 'bar-chart',
    })
  }

  function buildGoToItems(groups: MainMenuGroup[]): CommandPaletteItem[] {
    const items = groups
      .flatMap((group, groupIndex) => group.items.map((item, itemIndex) => ({ group, item, groupIndex, itemIndex })))
      .filter(({ item }) => !String(item.route ?? '').startsWith('/reports/'))
      .map(({ group, item, groupIndex, itemIndex }) => ({
        key: `page:${item.code}`,
        group: 'go-to' as const,
        kind: 'page' as const,
        scope: 'pages' as const,
        title: item.label,
        subtitle: group.label,
        icon: resolveIconName(item.icon, item.route.startsWith('/catalogs/') ? 'grid' : 'file-text'),
        badge: 'Page',
        hint: null,
        route: item.route,
        commandCode: null,
        status: null,
        openInNewTabSupported: true,
        keywords: [item.code, group.label],
        defaultRank: 650 - (groupIndex * 20) - itemIndex,
        score: 0,
        source: 'local' as const,
      }))

    const existingRoutes = new Set(items.map((item) => item.route))
    const fallbackPages = materializeLocalItems(
      (config.specialPageItems ?? []).filter((item) => !existingRoutes.has(item.route ?? '')),
      {
        source: 'local',
        score: 0,
        rankFrom: 560,
        iconFallback: 'file-text',
      },
    )

    return [...items, ...fallbackPages]
  }

  function buildRecentItems(): CommandPaletteItem[] {
    return recentEntries.value.map((entry, index) => ({
      key: `recent:${entry.key}`,
      group: 'recent',
      kind: 'recent',
      scope: entry.scope,
      title: entry.title,
      subtitle: entry.subtitle ?? null,
      icon: resolveIconName(entry.icon, entry.scope === 'reports' ? 'bar-chart' : 'file-text'),
      badge: entry.badge ?? null,
      hint: null,
      route: entry.route ?? null,
      commandCode: null,
      status: entry.status ?? null,
      openInNewTabSupported: entry.openInNewTabSupported,
      keywords: [],
      defaultRank: 500 - index,
      score: 0,
      isRecent: true,
      source: 'local',
    }))
  }

  return {
    isOpen,
    query,
    activeIndex,
    focusRequestKey,
    groups,
    flatItems,
    activeScope,
    cleanQuery,
    remoteError,
    hasRemoteError,
    remoteLoading,
    showRemoteLoading,
    hasResults,
    hydrate,
    open,
    close,
    setQuery,
    setCurrentRoute,
    setExplicitContext,
    clearExplicitContext,
    moveActive,
    setActiveIndex,
    executeActive,
    executeItem,
  }
})

function materializeLocalItems(
  items: CommandPaletteItemSeed[],
  options: {
    source: CommandPaletteItem['source']
    score: number
    rankFrom: number
    iconFallback: NgbIconName
    isCurrentContext?: boolean
    isFavorite?: boolean
    isRecent?: boolean
  },
): CommandPaletteItem[] {
  return items.map((item, index) => ({
    ...item,
    icon: resolveIconName(item.icon, item.scope === 'reports' ? 'bar-chart' : options.iconFallback),
    defaultRank: item.defaultRank || options.rankFrom - index,
    source: options.source,
    score: options.score,
    isCurrentContext: item.isCurrentContext ?? options.isCurrentContext,
    isFavorite: item.isFavorite ?? options.isFavorite,
    isRecent: item.isRecent ?? options.isRecent,
  }))
}

function dedupeSeedsByKey(items: CommandPaletteItemSeed[]): CommandPaletteItemSeed[] {
  const seen = new Set<string>()
  const result: CommandPaletteItemSeed[] = []

  for (const item of items) {
    if (!item || seen.has(item.key)) continue
    seen.add(item.key)
    result.push(item)
  }

  return result
}

function resolveGroupLabel(code: CommandPaletteGroupCode): string {
  switch (code) {
    case 'actions':
      return 'Actions'
    case 'go-to':
      return 'Go to'
    case 'documents':
      return 'Documents'
    case 'catalogs':
      return 'Catalogs'
    case 'reports':
      return 'Reports'
    case 'recent':
      return 'Recent'
  }
}

function visibleLimitForGroup(code: CommandPaletteGroupCode, query: string): number {
  if (query.trim().length > 0) return 8

  switch (code) {
    case 'actions':
      return 10
    case 'go-to':
      return 6
    case 'reports':
      return 6
    case 'recent':
      return 5
    default:
      return 6
  }
}

function limitGroupItems(code: CommandPaletteGroupCode, items: CommandPaletteItem[], query: string): CommandPaletteItem[] {
  return items
    .sort((a, b) => b.score - a.score || b.defaultRank - a.defaultRank || a.title.localeCompare(b.title))
    .slice(0, visibleLimitForGroup(code, query))
}

function scoreItems(items: CommandPaletteItem[], query: string, scope: CommandPaletteScope | null): CommandPaletteItem[] {
  const textQuery = query.trim()

  return items
    .filter((item) => item.scope === scope || scope == null)
    .map((item) => {
      if (!textQuery) {
        return {
          ...item,
          score: item.defaultRank,
        }
      }

      const score = scoreSearchText(textQuery, [
        ...defaultSearchFields(item.title, item.subtitle ?? '', item.badge ?? ''),
        ...defaultSearchFields(...(item.keywords ?? [])),
      ])

      if (score <= 0) return null

      const boostedScore = score
        + (item.isCurrentContext ? 0.08 : 0)
        + (item.isFavorite ? 0.04 : 0)
        + (item.isRecent ? 0.02 : 0)
        + (item.scope === 'commands' ? 0.01 : 0)

      return {
        ...item,
        score: boostedScore,
      }
    })
    .filter((item): item is CommandPaletteItem => !!item)
}

function dedupeItemsByKey(items: CommandPaletteItem[]): CommandPaletteItem[] {
  const seen = new Set<string>()
  const result: CommandPaletteItem[] = []

  for (const item of items) {
    if (!item || seen.has(item.key)) continue
    seen.add(item.key)
    result.push(item)
  }

  return result
}

function shouldRunRemoteSearch(query: string, scope: CommandPaletteScope | null, remoteEnabled: boolean): boolean {
  if (!remoteEnabled) return false
  if (query.trim().length < 2) return false
  if (scope === 'commands' || scope === 'pages' || scope === 'reports') return false
  return true
}

function buildSearchContext(context: CommandPaletteExplicitContext | null): CommandPaletteSearchContextDto | null {
  if (!context) return null

  return {
    entityType: context.entityType ?? null,
    documentType: context.documentType ?? null,
    catalogType: context.catalogType ?? null,
    entityId: looksLikeGuid(context.entityId) ? context.entityId : null,
  }
}

function looksLikeGuid(value: string | null | undefined): value is string {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(String(value ?? '').trim())
}

function scopeForRemoteKind(kind: string): CommandPaletteScope {
  switch (kind) {
    case 'document':
      return 'documents'
    case 'catalog':
      return 'catalogs'
    case 'report':
      return 'reports'
    default:
      return 'pages'
  }
}

function isRecentTrackable(item: CommandPaletteItem): boolean {
  return Boolean(item.route) && (item.kind === 'page' || item.kind === 'document' || item.kind === 'catalog' || item.kind === 'report' || item.kind === 'recent')
}

function openRouteInNewTab(route: string): void {
  const { router } = getConfiguredNgbCommandPalette()
  const href = router.resolve(route).href
  if (typeof window === 'undefined') return
  window.open(href, '_blank', 'noopener,noreferrer')
}

function resolveIconName(icon: string | null | undefined, fallback: NgbIconName): NgbIconName {
  const value = String(icon ?? '').trim()
  if (!value) return fallback
  if (isNgbIconName(value)) return value

  switch (value) {
    case 'list':
    case 'receipt':
    case 'book-open':
    case 'calculator':
    case 'users':
    case 'building-2':
    case 'coins':
    case 'wallet':
    case 'wrench':
    case 'clipboard-list':
    case 'check-square':
    case 'tag':
    case 'landmark':
    case 'calendar-check':
    case 'scale':
    case 'git-merge':
    case 'shield-check':
    case 'heart-pulse':
    case 'cogs':
      return value
    case 'chart':
      return 'bar-chart'
    case 'file':
      return 'file-text'
    case 'book':
      return 'book-open'
    case 'folder':
      return 'grid'
    default:
      return fallback
  }
}

