import {
  computed,
  getCurrentScope,
  getCurrentInstance,
  inject,
  onScopeDispose,
  provide,
  readonly,
  ref,
  type InjectionKey,
  type Ref,
} from 'vue'

import {
  getConfiguredNgbWorkCenter,
  type NgbWorkCenterConfig,
  type WorkCenterRealtimeClient,
  type WorkCenterSessionSnapshot,
} from './config'
import type {
  NotificationChannel,
  NotificationPreference,
  WorkCenterItem,
  WorkCenterPage,
  WorkCenterQuery,
  WorkCenterSummary,
} from './types'

interface WorkCenterFeedSession {
  clear: () => void
  isActive: () => boolean
  refresh: () => Promise<void>
}

export type NgbWorkCenterRuntime = {
  readonly summary: Ref<WorkCenterSummary | null>
  readonly config: NgbWorkCenterConfig
  readonly vertical: string | null
  refreshSummary: () => Promise<void>
  refreshActiveFeeds: () => Promise<void>
  connectRealtime: () => Promise<void>
  registerFeed: (session: WorkCenterFeedSession) => () => void
  clearUnauthorized: (cause: unknown) => void
  dispose: () => Promise<void>
}

const workCenterRuntimeKey: InjectionKey<NgbWorkCenterRuntime> = Symbol('ngb-work-center-runtime')

function normalizeVertical(value: string | null | undefined): string | null {
  return String(value ?? '').trim() || null
}

function toMessage(cause: unknown): string {
  return cause instanceof Error && cause.message.trim()
    ? cause.message
    : 'Unable to load Work Center.'
}

export function createNgbWorkCenterRuntime(options: {
  vertical?: string | null
  config?: NgbWorkCenterConfig
} = {}): NgbWorkCenterRuntime {
  const config = options.config ?? getConfiguredNgbWorkCenter()
  const vertical = normalizeVertical(options.vertical)
  const summary = ref<WorkCenterSummary | null>(null)
  const sessions = new Set<WorkCenterFeedSession>()

  let summaryPromise: Promise<void> | null = null
  let summarySequence = 0
  let lastInvalidationVersion = 0
  let realtimeClient: WorkCenterRealtimeClient | null = null
  let realtimePromise: Promise<void> | null = null
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let realtimeRequested = false
  let sessionSequence = 0
  let disposed = false
  let currentSession = config.session.getSnapshot()

  function clear(): void {
    summarySequence += 1
    // The underlying request cannot be cancelled, but it belongs to the old
    // authenticated subject and must not block a fresh subject-scoped load.
    summaryPromise = null
    summary.value = null
    lastInvalidationVersion = 0
    for (const session of sessions) session.clear()
  }

  function clearUnauthorized(cause: unknown): void {
    if (config.isUnauthorizedError?.(cause)) clear()
  }

  async function refreshSummary(): Promise<void> {
    if (summaryPromise) return summaryPromise
    const sequence = ++summarySequence
    const request = (async () => {
      try {
        const result = await config.gateway.getSummary(vertical)
        if (disposed || sequence !== summarySequence) return
        summary.value = result
        lastInvalidationVersion = Math.max(lastInvalidationVersion, result.version)
      } catch (cause) {
        clearUnauthorized(cause)
        throw cause
      }
    })()
    summaryPromise = request
    try {
      await request
    } finally {
      if (summaryPromise === request) summaryPromise = null
    }
  }

  async function refreshActiveFeeds(): Promise<void> {
    const active = [...sessions].filter((session) => session.isActive())
    if (active.length === 0) {
      await refreshSummary()
      return
    }
    await Promise.allSettled(active.map((session) => session.refresh()))
  }

  function cancelReconnect(): void {
    if (reconnectTimer === null) return
    clearTimeout(reconnectTimer)
    reconnectTimer = null
  }

  function scheduleReconnect(): void {
    if (disposed || !realtimeRequested || !currentSession.authenticated || reconnectTimer !== null) return
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null
      void connectRealtime()
    }, 30_000)
  }

  async function stopRealtime(): Promise<void> {
    cancelReconnect()
    const client = realtimeClient
    realtimeClient = null
    realtimePromise = null
    if (client) await client.stop().catch(() => undefined)
  }

  async function connectRealtime(): Promise<void> {
    realtimeRequested = true
    if (disposed || !currentSession.authenticated || realtimeClient || realtimePromise) {
      return realtimePromise ?? undefined
    }

    cancelReconnect()
    const client = config.createRealtimeClient()
    realtimeClient = client
    realtimePromise = client.start({
      changed: (version) => {
        if (Number.isFinite(version) && version <= lastInvalidationVersion) return
        if (Number.isFinite(version)) lastInvalidationVersion = version
        void refreshActiveFeeds().catch(() => undefined)
      },
      reconnected: () => {
        void refreshActiveFeeds().catch(() => undefined)
      },
      disconnected: () => {
        if (realtimeClient === client) realtimeClient = null
        scheduleReconnect()
      },
    }).catch(() => {
      if (realtimeClient === client) realtimeClient = null
      scheduleReconnect()
    }).finally(() => {
      realtimePromise = null
    })
    await realtimePromise
  }

  function registerFeed(session: WorkCenterFeedSession): () => void {
    sessions.add(session)
    return () => sessions.delete(session)
  }

  function sessionChanged(next: WorkCenterSessionSnapshot): void {
    const identityChanged = currentSession.subject !== next.subject
    const signedOut = currentSession.authenticated && !next.authenticated
    currentSession = next
    const sequence = ++sessionSequence
    if (identityChanged || signedOut) clear()
    void (async () => {
      if (!next.authenticated || identityChanged) await stopRealtime()
      if (disposed || sequence !== sessionSequence) return
      if (next.authenticated && realtimeRequested) await connectRealtime()
    })().catch(() => undefined)
  }

  const unsubscribeSession = config.session.subscribe(sessionChanged)

  async function dispose(): Promise<void> {
    if (disposed) return
    disposed = true
    sessionSequence += 1
    unsubscribeSession()
    clear()
    sessions.clear()
    await stopRealtime()
  }

  return {
    summary,
    config,
    vertical,
    refreshSummary,
    refreshActiveFeeds,
    connectRealtime,
    registerFeed,
    clearUnauthorized,
    dispose,
  }
}

export function provideNgbWorkCenterRuntime(options: {
  vertical?: string | null
  config?: NgbWorkCenterConfig
} = {}): NgbWorkCenterRuntime {
  const runtime = createNgbWorkCenterRuntime(options)
  provide(workCenterRuntimeKey, runtime)
  if (getCurrentScope()) onScopeDispose(() => { void runtime.dispose() })
  return runtime
}

export function useNgbWorkCenterRuntime(options: {
  vertical?: string | null
  runtime?: NgbWorkCenterRuntime
} = {}): { runtime: NgbWorkCenterRuntime; owned: boolean } {
  if (options.runtime) return { runtime: options.runtime, owned: false }
  const injected = getCurrentInstance() ? inject(workCenterRuntimeKey, null) : null
  if (injected) return { runtime: injected, owned: false }
  return {
    runtime: createNgbWorkCenterRuntime({ vertical: options.vertical }),
    owned: true,
  }
}

export function useWorkCenter(options: {
  vertical?: string | null
  runtime?: NgbWorkCenterRuntime
} = {}) {
  const { runtime, owned } = useNgbWorkCenterRuntime(options)
  const items = ref<WorkCenterItem[]>([])
  const nextCursor = ref<string | null>(null)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref<string | null>(null)
  const loadMoreError = ref<string | null>(null)
  const activeQuery = ref<WorkCenterQuery>({ limit: 30, vertical: runtime.vertical })
  let refreshPromise: Promise<void> | null = null
  let feedController: AbortController | null = null
  let feedSequence = 0
  let active = false
  let disposed = false

  function clearFeed(): void {
    feedController?.abort()
    feedController = null
    active = false
    items.value = []
    nextCursor.value = null
    loading.value = false
    loadingMore.value = false
    loadMoreError.value = null
  }

  async function load(query: WorkCenterQuery = {}, append = false): Promise<void> {
    const normalized = {
      limit: 30,
      vertical: runtime.vertical,
      ...query,
    }
    if (append && !normalized.cursor) return
    active = true
    feedController?.abort()
    const controller = new AbortController()
    feedController = controller
    const sequence = ++feedSequence
    if (append) loadingMore.value = true
    else {
      loading.value = true
      nextCursor.value = null
    }
    if (append) loadMoreError.value = null
    else {
      error.value = null
      loadMoreError.value = null
    }

    try {
      const page: WorkCenterPage = await runtime.config.gateway.getItems(normalized, controller.signal)
      if (sequence !== feedSequence || disposed) return
      activeQuery.value = { ...normalized, cursor: null }
      if (append) {
        const existing = new Set(items.value.map((item) => `${item.kind}:${item.id}`))
        items.value = [
          ...items.value,
          ...page.items.filter((item) => !existing.has(`${item.kind}:${item.id}`)),
        ]
      } else {
        items.value = page.items
      }
      nextCursor.value = page.nextCursor ?? null
      if (!append) await runtime.refreshSummary()
    } catch (cause) {
      if (controller.signal.aborted || disposed) return
      runtime.clearUnauthorized(cause)
      if (append && items.value.length > 0) loadMoreError.value = toMessage(cause)
      else error.value = toMessage(cause)
      throw cause
    } finally {
      if (sequence === feedSequence && !disposed) {
        loading.value = false
        loadingMore.value = false
        feedController = null
      }
    }
  }

  async function refresh(): Promise<void> {
    if (refreshPromise) return refreshPromise
    refreshPromise = load(activeQuery.value).finally(() => { refreshPromise = null })
    return refreshPromise
  }

  async function loadMore(): Promise<void> {
    if (!nextCursor.value || loadingMore.value) return
    await load({ ...activeQuery.value, cursor: nextCursor.value }, true)
  }

  async function mutate(operation: () => Promise<void>): Promise<void> {
    error.value = null
    try {
      await operation()
      await runtime.refreshActiveFeeds()
    } catch (cause) {
      runtime.clearUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  async function optimisticMarkRead(item: WorkCenterItem): Promise<void> {
    const index = items.value.findIndex((candidate) =>
      candidate.id === item.id && candidate.kind === item.kind)
    const previousItems = items.value
    const previousSummary = runtime.summary.value
    if (index >= 0 && !item.isRead) {
      const updated = [...items.value]
      updated[index] = { ...updated[index]!, isRead: true }
      items.value = updated
      if (item.kind === 'Notification' && runtime.summary.value) {
        runtime.summary.value = {
          ...runtime.summary.value,
          attentionCount: Math.max(0, runtime.summary.value.attentionCount - 1),
          unreadNotificationCount: Math.max(0, runtime.summary.value.unreadNotificationCount - 1),
        }
      }
    }

    try {
      await (item.kind === 'Task'
        ? runtime.config.gateway.markTaskRead(item.id)
        : runtime.config.gateway.markNotificationRead(item.id))
      await runtime.refreshActiveFeeds()
    } catch (cause) {
      items.value = previousItems
      runtime.summary.value = previousSummary
      runtime.clearUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  async function optimisticDismiss(item: WorkCenterItem): Promise<void> {
    const previousItems = items.value
    const previousSummary = runtime.summary.value
    items.value = items.value.filter((candidate) =>
      candidate.id !== item.id || candidate.kind !== item.kind)
    if (runtime.summary.value) {
      runtime.summary.value = {
        ...runtime.summary.value,
        attentionCount: item.isRead
          ? runtime.summary.value.attentionCount
          : Math.max(0, runtime.summary.value.attentionCount - 1),
        notificationCount: Math.max(0, runtime.summary.value.notificationCount - 1),
        unreadNotificationCount: item.isRead
          ? runtime.summary.value.unreadNotificationCount
          : Math.max(0, runtime.summary.value.unreadNotificationCount - 1),
      }
    }

    try {
      await runtime.config.gateway.dismissNotification(item.id)
      await runtime.refreshActiveFeeds()
    } catch (cause) {
      items.value = previousItems
      runtime.summary.value = previousSummary
      runtime.clearUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  const unregister = runtime.registerFeed({
    clear: clearFeed,
    isActive: () => active && !disposed,
    refresh,
  })

  if (getCurrentScope()) {
    onScopeDispose(() => {
      disposed = true
      unregister()
      feedController?.abort()
      if (owned) void runtime.dispose()
    })
  }

  return {
    summary: readonly(runtime.summary),
    items: readonly(items),
    nextCursor: readonly(nextCursor),
    loading: readonly(loading),
    loadingMore: readonly(loadingMore),
    error: readonly(error),
    loadMoreError: readonly(loadMoreError),
    attentionCount: computed(() => runtime.summary.value?.attentionCount ?? 0),
    refreshSummary: runtime.refreshSummary,
    load,
    refresh,
    loadMore,
    connectRealtime: runtime.connectRealtime,
    markRead: optimisticMarkRead,
    dismiss: optimisticDismiss,
    claim: (item: WorkCenterItem) => mutate(() =>
      runtime.config.gateway.claimTask(item.id, item.version)),
    snooze: (item: WorkCenterItem, snoozedUntilUtc: string) =>
      mutate(() => runtime.config.gateway.snoozeTask(item.id, snoozedUntilUtc)),
  }
}

export function useWorkCenterPreferences(options: {
  vertical?: string | null
  runtime?: NgbWorkCenterRuntime
} = {}) {
  const { runtime, owned } = useNgbWorkCenterRuntime(options)
  if (owned && getCurrentScope()) onScopeDispose(() => { void runtime.dispose() })
  return {
    load: (): Promise<NotificationPreference[]> => runtime.config.gateway.getPreferences(),
    save: (
      preferences: Array<{ code: string; channel: NotificationChannel; isEnabled: boolean }>,
    ): Promise<void> => runtime.config.gateway.updatePreferences(preferences),
  }
}
