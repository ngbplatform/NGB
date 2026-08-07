import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr'
import { computed, getCurrentScope, onScopeDispose, readonly, ref } from 'vue'

import { getAccessToken, getAuthSnapshot } from '../auth/keycloak'
import { ApiError } from '../api/http'
import { readAppEnv } from '../env/runtimeConfig'
import {
  claimWorkCenterTask,
  dismissWorkCenterNotification,
  getWorkCenterItems,
  getWorkCenterSummary,
  markWorkCenterNotificationRead,
  markWorkCenterTaskRead,
  snoozeWorkCenterTask,
} from './api'
import type { WorkCenterItem, WorkCenterPage, WorkCenterQuery, WorkCenterSummary } from './types'

const summary = ref<WorkCenterSummary | null>(null)

interface WorkCenterFeedSession {
  clear: () => void
  isActive: () => boolean
  refreshIfActive: () => Promise<void>
}

const feedSessions = new Set<WorkCenterFeedSession>()
let connection: HubConnection | null = null
let connectPromise: Promise<void> | null = null
let reconnectTimer: number | null = null
let summaryPromise: Promise<void> | null = null
let summaryPromiseVertical: string | null = null
let summaryVertical: string | null = null
let summarySequence = 0
let lastInvalidationVersion = 0

function apiBaseUrl(): string {
  const configured = readAppEnv('VITE_API_BASE_URL')
  return configured.length > 0 ? configured : window.location.origin
}

function toMessage(cause: unknown): string {
  return cause instanceof Error && cause.message.trim() ? cause.message : 'Unable to load Work Center.'
}

async function refreshSummary(vertical: string | null = summaryVertical): Promise<void> {
  const normalizedVertical = vertical?.trim() || null
  summaryVertical = normalizedVertical
  if (summaryPromise && summaryPromiseVertical === normalizedVertical) return summaryPromise
  const sequence = ++summarySequence
  const request = (async () => {
    try {
      const result = await getWorkCenterSummary(normalizedVertical)
      if (sequence !== summarySequence) return
      summary.value = result
      lastInvalidationVersion = Math.max(lastInvalidationVersion, result.version)
    } catch (cause) {
      clearOnUnauthorized(cause)
      throw cause
    }
  })()
  summaryPromise = request
  summaryPromiseVertical = normalizedVertical
  try {
    await request
  } finally {
    if (summaryPromise === request) {
      summaryPromise = null
      summaryPromiseVertical = null
    }
  }
}

function clearOnUnauthorized(cause: unknown): void {
  if (!(cause instanceof ApiError) || (cause.status !== 401 && cause.status !== 403)) return
  summary.value = null
  for (const session of feedSessions) session.clear()
}

async function refreshActiveFeeds(): Promise<void> {
  const sessions = [...feedSessions].filter((session) => session.isActive())
  if (sessions.length === 0) {
    await refreshSummary()
    return
  }
  await Promise.allSettled(sessions.map((session) => session.refreshIfActive()))
}

async function connectRealtime(): Promise<void> {
  if (connection || connectPromise || typeof window === 'undefined') return connectPromise ?? undefined

  connectPromise = (async () => {
    connection = new HubConnectionBuilder()
      .withUrl(new URL('/hubs/work-center', apiBaseUrl()).toString(), {
        accessTokenFactory: async () => {
          const auth = getAuthSnapshot()
          if (!auth.initialized || !auth.authenticated) return auth.token ?? ''
          return await getAccessToken() ?? ''
        },
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      })
      .configureLogging(LogLevel.Error)
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
      .build()

    connection.on('workCenterChanged', (version: number) => {
      if (Number.isFinite(version) && version <= lastInvalidationVersion) return
      if (Number.isFinite(version)) lastInvalidationVersion = version
      void refreshActiveFeeds().catch(() => undefined)
    })
    connection.onreconnected(() => {
      void refreshActiveFeeds().catch(() => undefined)
    })
    connection.onclose(() => {
      connection = null
      scheduleReconnect()
    })

    try {
      await connection.start()
    } catch {
      // HTTP remains authoritative; the next shell mount or reconnect will retry.
      await connection.stop().catch(() => undefined)
      connection = null
      scheduleReconnect()
    }
  })().finally(() => { connectPromise = null })

  await connectPromise
}

function scheduleReconnect(): void {
  if (typeof window === 'undefined' || reconnectTimer !== null) return
  reconnectTimer = window.setTimeout(() => {
    reconnectTimer = null
    void connectRealtime()
  }, 30_000)
}

export function useWorkCenter() {
  const items = ref<WorkCenterItem[]>([])
  const nextCursor = ref<string | null>(null)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref<string | null>(null)
  const loadMoreError = ref<string | null>(null)
  const activeQuery = ref<WorkCenterQuery>({ limit: 30 })
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
    const normalized = { limit: 30, ...query }
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
      const page: WorkCenterPage = await getWorkCenterItems(normalized, controller.signal)
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
      if (!append) await refreshSummary(normalized.vertical ?? null)
    } catch (cause) {
      if (controller.signal.aborted || disposed) return
      clearOnUnauthorized(cause)
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
      await refreshActiveFeeds()
    } catch (cause) {
      clearOnUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  async function optimisticMarkRead(item: WorkCenterItem): Promise<void> {
    const index = items.value.findIndex((candidate) =>
      candidate.id === item.id && candidate.kind === item.kind)
    const previousItems = items.value
    const previousSummary = summary.value
    if (index >= 0 && !item.isRead) {
      const updated = [...items.value]
      updated[index] = { ...updated[index]!, isRead: true }
      items.value = updated
      if (item.kind === 'Notification' && summary.value) {
        summary.value = {
          ...summary.value,
          attentionCount: Math.max(0, summary.value.attentionCount - 1),
          unreadNotificationCount: Math.max(0, summary.value.unreadNotificationCount - 1),
        }
      }
    }

    try {
      await (item.kind === 'Task'
        ? markWorkCenterTaskRead(item.id)
        : markWorkCenterNotificationRead(item.id))
      await refreshActiveFeeds()
    } catch (cause) {
      items.value = previousItems
      summary.value = previousSummary
      clearOnUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  async function optimisticDismiss(item: WorkCenterItem): Promise<void> {
    const previousItems = items.value
    const previousSummary = summary.value
    items.value = items.value.filter((candidate) =>
      candidate.id !== item.id || candidate.kind !== item.kind)
    if (summary.value) {
      summary.value = {
        ...summary.value,
        attentionCount: item.isRead
          ? summary.value.attentionCount
          : Math.max(0, summary.value.attentionCount - 1),
        notificationCount: Math.max(0, summary.value.notificationCount - 1),
        unreadNotificationCount: item.isRead
          ? summary.value.unreadNotificationCount
          : Math.max(0, summary.value.unreadNotificationCount - 1),
      }
    }

    try {
      await dismissWorkCenterNotification(item.id)
      await refreshActiveFeeds()
    } catch (cause) {
      items.value = previousItems
      summary.value = previousSummary
      clearOnUnauthorized(cause)
      error.value = toMessage(cause)
      throw cause
    }
  }

  const session: WorkCenterFeedSession = {
    clear: clearFeed,
    isActive: () => active && !disposed,
    // refreshActiveFeeds already filters sessions through isActive(). Keeping a
    // second conditional here creates an unreachable branch and no extra safety.
    refreshIfActive: refresh,
  }
  feedSessions.add(session)

  if (getCurrentScope()) {
    onScopeDispose(() => {
      disposed = true
      feedSessions.delete(session)
      feedController?.abort()
    })
  }

  return {
    summary: readonly(summary),
    items: readonly(items),
    nextCursor: readonly(nextCursor),
    loading: readonly(loading),
    loadingMore: readonly(loadingMore),
    error: readonly(error),
    loadMoreError: readonly(loadMoreError),
    attentionCount: computed(() => summary.value?.attentionCount ?? 0),
    refreshSummary,
    load,
    refresh,
    loadMore,
    connectRealtime,
    markRead: optimisticMarkRead,
    dismiss: optimisticDismiss,
    claim: (item: WorkCenterItem) => mutate(() => claimWorkCenterTask(item.id, item.version)),
    snooze: (item: WorkCenterItem, snoozedUntilUtc: string) =>
      mutate(() => snoozeWorkCenterTask(item.id, snoozedUntilUtc)),
  }
}
