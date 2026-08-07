import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'

import type {
  WorkCenterItem,
  WorkCenterPage,
  WorkCenterSummary,
} from '../../../../src/ngb/work-center/types'

const api = vi.hoisted(() => ({
  claim: vi.fn(),
  dismiss: vi.fn(),
  items: vi.fn(),
  markNotificationRead: vi.fn(),
  markTaskRead: vi.fn(),
  snooze: vi.fn(),
  summary: vi.fn(),
}))

const auth = vi.hoisted(() => ({
  getAccessToken: vi.fn(),
  getSnapshot: vi.fn(),
}))

const environment = vi.hoisted(() => ({
  read: vi.fn(),
}))

const signalr = vi.hoisted(() => ({
  builder: {
    build: vi.fn(),
    configureLogging: vi.fn(),
    withAutomaticReconnect: vi.fn(),
    withUrl: vi.fn(),
  },
  connection: {
    on: vi.fn(),
    onclose: vi.fn(),
    onreconnected: vi.fn(),
    start: vi.fn(),
    stop: vi.fn(),
  },
  handlers: new Map<string, (...args: never[]) => unknown>(),
  closeHandler: null as null | (() => void),
  reconnectedHandler: null as null | (() => void),
}))

vi.mock('../../../../src/ngb/work-center/api', () => ({
  claimWorkCenterTask: api.claim,
  dismissWorkCenterNotification: api.dismiss,
  getWorkCenterItems: api.items,
  getWorkCenterSummary: api.summary,
  markWorkCenterNotificationRead: api.markNotificationRead,
  markWorkCenterTaskRead: api.markTaskRead,
  snoozeWorkCenterTask: api.snooze,
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAccessToken: auth.getAccessToken,
  getAuthSnapshot: auth.getSnapshot,
}))

vi.mock('../../../../src/ngb/env/runtimeConfig', () => ({
  readAppEnv: environment.read,
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  ApiError: class ApiError extends Error {
    readonly status: number
    readonly url: string

    constructor(args: { message: string; status: number; url: string }) {
      super(args.message)
      this.name = 'ApiError'
      this.status = args.status
      this.url = args.url
    }
  },
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(function MockHubConnectionBuilder() {
    return signalr.builder
  }),
  HttpTransportType: {
    WebSockets: 1,
    LongPolling: 2,
  },
  LogLevel: {
    Error: 4,
  },
}))

function task(overrides: Partial<WorkCenterItem> = {}): WorkCenterItem {
  return {
    id: 'task-1',
    kind: 'Task',
    code: 'pm.payment.apply',
    title: 'Apply payment',
    description: 'Apply the posted payment.',
    source: {
      resourceKind: 'Document',
      resourceCode: 'pm.receivable-payment',
      entityId: 'payment-1',
      title: 'Payment 1',
    },
    priority: 'High',
    taskStatus: 'Open',
    sortAtUtc: '2026-07-26T15:00:00.000Z',
    isOverdue: false,
    isRead: false,
    version: 3,
    ...overrides,
  }
}

function notification(overrides: Partial<WorkCenterItem> = {}): WorkCenterItem {
  return task({
    id: 'notification-1',
    kind: 'Notification',
    code: 'pm.payment.posted',
    title: 'Payment posted',
    priority: null,
    severity: 'Information',
    taskStatus: null,
    ...overrides,
  })
}

function page(items: WorkCenterItem[], nextCursor: string | null = null): WorkCenterPage {
  return { items, nextCursor, limit: 30 }
}

function summary(overrides: Partial<WorkCenterSummary> = {}): WorkCenterSummary {
  return {
    attentionCount: 2,
    openTaskCount: 1,
    overdueTaskCount: 0,
    notificationCount: 1,
    unreadNotificationCount: 1,
    version: 4,
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

function installWindow(origin = 'https://app.example') {
  vi.stubGlobal('window', {
    location: { origin },
    setTimeout: globalThis.setTimeout.bind(globalThis),
  })
}

async function freshWorkCenter() {
  vi.resetModules()
  return await import('../../../../src/ngb/work-center/useWorkCenter')
}

async function createApiError(status: number, message = `HTTP ${status}`) {
  const { ApiError } = await import('../../../../src/ngb/api/http')
  return new ApiError({ message, status, url: '/api/work-center/items' })
}

async function flush() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()

  api.items.mockResolvedValue(page([]))
  api.summary.mockResolvedValue(summary())
  api.claim.mockResolvedValue(undefined)
  api.dismiss.mockResolvedValue(undefined)
  api.markNotificationRead.mockResolvedValue(undefined)
  api.markTaskRead.mockResolvedValue(undefined)
  api.snooze.mockResolvedValue(undefined)

  auth.getSnapshot.mockReturnValue({
    initialized: true,
    authenticated: true,
    token: 'snapshot-token',
  })
  auth.getAccessToken.mockResolvedValue('fresh-token')
  environment.read.mockReturnValue('')

  signalr.handlers.clear()
  signalr.closeHandler = null
  signalr.reconnectedHandler = null
  signalr.builder.withUrl.mockImplementation(() => signalr.builder)
  signalr.builder.configureLogging.mockImplementation(() => signalr.builder)
  signalr.builder.withAutomaticReconnect.mockImplementation(() => signalr.builder)
  signalr.builder.build.mockImplementation(() => signalr.connection)
  signalr.connection.on.mockImplementation((name: string, handler: (...args: never[]) => unknown) => {
    signalr.handlers.set(name, handler)
  })
  signalr.connection.onclose.mockImplementation((handler: () => void) => {
    signalr.closeHandler = handler
  })
  signalr.connection.onreconnected.mockImplementation((handler: () => void) => {
    signalr.reconnectedHandler = handler
  })
  signalr.connection.start.mockResolvedValue(undefined)
  signalr.connection.stop.mockResolvedValue(undefined)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useWorkCenter', () => {
  it('loads, summarizes, appends cursor pages, and ignores load-more without a cursor', async () => {
    installWindow()
    api.items
      .mockResolvedValueOnce(page([task()], 'cursor-2'))
      .mockResolvedValueOnce(page([task(), notification()]))
    api.summary.mockResolvedValueOnce(summary())
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    expect(workCenter.attentionCount.value).toBe(0)
    await workCenter.load({ tab: 'tasks', limit: 1 })

    expect(workCenter.items.value).toEqual([task()])
    expect(workCenter.nextCursor.value).toBe('cursor-2')
    expect(workCenter.attentionCount.value).toBe(2)
    expect(workCenter.loading.value).toBe(false)
    expect(workCenter.error.value).toBeNull()

    await workCenter.loadMore()

    expect(api.items).toHaveBeenLastCalledWith({
      tab: 'tasks',
      limit: 1,
      cursor: 'cursor-2',
    }, expect.any(AbortSignal))
    expect(workCenter.items.value).toEqual([task(), notification()])
    expect(workCenter.nextCursor.value).toBeNull()
    expect(workCenter.loadingMore.value).toBe(false)
    expect(api.summary).toHaveBeenCalledTimes(1)

    await workCenter.loadMore()
    expect(api.items).toHaveBeenCalledTimes(2)

    await workCenter.load({}, true)
    expect(api.items).toHaveBeenCalledTimes(2)
  })

  it('deduplicates summary and refresh requests and suppresses duplicate load-more calls', async () => {
    installWindow()
    const summaryRequest = deferred<WorkCenterSummary>()
    api.summary.mockReturnValueOnce(summaryRequest.promise)
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    const firstSummary = workCenter.refreshSummary()
    const secondSummary = workCenter.refreshSummary()
    expect(api.summary).toHaveBeenCalledTimes(1)
    summaryRequest.resolve(summary())
    await Promise.all([firstSummary, secondSummary])

    const feedRequest = deferred<WorkCenterPage>()
    api.items.mockReturnValueOnce(feedRequest.promise)
    const firstRefresh = workCenter.refresh()
    const secondRefresh = workCenter.refresh()
    expect(api.items).toHaveBeenCalledTimes(1)
    feedRequest.resolve(page([task()], 'next'))
    await Promise.all([firstRefresh, secondRefresh])

    const moreRequest = deferred<WorkCenterPage>()
    api.items.mockReturnValueOnce(moreRequest.promise)
    const firstMore = workCenter.loadMore()
    const secondMore = workCenter.loadMore()
    expect(api.items).toHaveBeenCalledTimes(2)
    moreRequest.resolve(page([]))
    await Promise.all([firstMore, secondMore])
  })

  it('keeps loaded rows visible when a later cursor page fails and supports retry', async () => {
    installWindow()
    api.items
      .mockResolvedValueOnce(page([task()], 'cursor-2'))
      .mockRejectedValueOnce(new Error('next page failed'))
      .mockResolvedValueOnce(page([notification()]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    await workCenter.load({ tab: 'attention' })
    await expect(workCenter.loadMore()).rejects.toThrow('next page failed')

    expect(workCenter.items.value).toEqual([task()])
    expect(workCenter.error.value).toBeNull()
    expect(workCenter.loadMoreError.value).toBe('next page failed')
    expect(workCenter.nextCursor.value).toBe('cursor-2')

    await workCenter.loadMore()
    expect(workCenter.items.value).toEqual([task(), notification()])
    expect(workCenter.loadMoreError.value).toBeNull()
  })

  it('aborts superseded feeds and ignores their stale successful result', async () => {
    installWindow()
    const staleRequest = deferred<WorkCenterPage>()
    api.items
      .mockReturnValueOnce(staleRequest.promise)
      .mockResolvedValueOnce(page([notification()]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    const staleLoad = workCenter.load({ tab: 'tasks' })
    const staleSignal = api.items.mock.calls[0]?.[1] as AbortSignal
    const currentLoad = workCenter.load({ tab: 'notifications' })

    expect(staleSignal.aborted).toBe(true)
    await currentLoad
    staleRequest.resolve(page([task()]))
    await staleLoad

    expect(workCenter.items.value).toEqual([notification()])
  })

  it('isolates feed queries, requests, and items between simultaneous consumers', async () => {
    installWindow()
    const tasksRequest = deferred<WorkCenterPage>()
    api.items.mockImplementation((query: { tab?: string }) =>
      query.tab === 'tasks'
        ? tasksRequest.promise
        : Promise.resolve(page([notification()])))
    const { useWorkCenter } = await freshWorkCenter()
    const fullPage = useWorkCenter()
    const drawer = useWorkCenter()

    const fullPageLoad = fullPage.load({ tab: 'tasks' })
    const fullPageSignal = api.items.mock.calls[0]?.[1] as AbortSignal
    await drawer.load({ tab: 'notifications', limit: 20 })

    expect(fullPageSignal.aborted).toBe(false)
    expect(drawer.items.value).toEqual([notification()])

    tasksRequest.resolve(page([task()]))
    await fullPageLoad

    expect(fullPage.items.value).toEqual([task()])
    expect(drawer.items.value).toEqual([notification()])
  })

  it('suppresses an aborted feed rejection without exposing an error', async () => {
    installWindow()
    const staleRequest = deferred<WorkCenterPage>()
    api.items
      .mockReturnValueOnce(staleRequest.promise)
      .mockResolvedValueOnce(page([]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    const staleLoad = workCenter.load({ tab: 'tasks' })
    await workCenter.load({ tab: 'attention' })
    staleRequest.reject(new Error('aborted request'))
    await staleLoad

    expect(workCenter.error.value).toBeNull()
  })

  it('surfaces safe feed errors and clears state on both unauthorized statuses', async () => {
    installWindow()
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    api.items.mockRejectedValueOnce(new Error('network unavailable'))
    await expect(workCenter.load()).rejects.toThrow('network unavailable')
    expect(workCenter.error.value).toBe('network unavailable')

    api.items.mockRejectedValueOnce(new Error('   '))
    await expect(workCenter.load()).rejects.toThrow()
    expect(workCenter.error.value).toBe('Unable to load Work Center.')

    api.items.mockRejectedValueOnce('gateway failure')
    await expect(workCenter.load()).rejects.toBe('gateway failure')
    expect(workCenter.error.value).toBe('Unable to load Work Center.')

    api.items.mockRejectedValueOnce(await createApiError(401))
    await expect(workCenter.load()).rejects.toMatchObject({ status: 401 })
    expect(workCenter.items.value).toEqual([])
    expect(workCenter.summary.value).toBeNull()
    expect(workCenter.nextCursor.value).toBeNull()

    await workCenter.load()
    api.items.mockRejectedValueOnce(await createApiError(403))
    await expect(workCenter.load()).rejects.toMatchObject({ status: 403 })
    expect(workCenter.items.value).toEqual([])
  })

  it('propagates summary failures, resets its in-flight guard, and clears unauthorized data', async () => {
    installWindow()
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    api.summary.mockRejectedValueOnce(await createApiError(403, 'forbidden'))
    await expect(workCenter.refreshSummary()).rejects.toThrow('forbidden')
    expect(workCenter.items.value).toEqual([])
    expect(workCenter.summary.value).toBeNull()

    api.summary.mockResolvedValueOnce(summary({ version: 9 }))
    await workCenter.refreshSummary()
    expect(workCenter.summary.value?.version).toBe(9)
  })

  it('keeps the newest summary when different vertical requests finish out of order', async () => {
    installWindow()
    const pmSummary = deferred<WorkCenterSummary>()
    const crmSummary = deferred<WorkCenterSummary>()
    api.summary.mockImplementation((vertical: string | null) =>
      vertical === 'pm' ? pmSummary.promise : crmSummary.promise)
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    const staleRequest = workCenter.refreshSummary(' pm ')
    const currentRequest = workCenter.refreshSummary('crm')
    pmSummary.resolve(summary({ attentionCount: 99, version: 10 }))
    await staleRequest
    expect(workCenter.summary.value).toBeNull()

    crmSummary.resolve(summary({ attentionCount: 7, version: 11 }))
    await currentRequest
    expect(api.summary).toHaveBeenNthCalledWith(1, 'pm')
    expect(api.summary).toHaveBeenNthCalledWith(2, 'crm')
    expect(workCenter.summary.value?.attentionCount).toBe(7)
    expect(workCenter.summary.value?.version).toBe(11)
  })

  it('claims and snoozes tasks, refreshes after success, and preserves actionable errors', async () => {
    installWindow()
    api.items.mockResolvedValue(page([task()]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    await workCenter.claim(task())
    expect(api.claim).toHaveBeenCalledWith('task-1', 3)

    await workCenter.snooze(task(), '2026-07-27T15:00:00.000Z')
    expect(api.snooze).toHaveBeenCalledWith('task-1', '2026-07-27T15:00:00.000Z')

    api.claim.mockRejectedValueOnce(new Error('claim lost'))
    await expect(workCenter.claim(task())).rejects.toThrow('claim lost')
    expect(workCenter.error.value).toBe('claim lost')
  })

  it('optimistically marks task and notification items read and updates notification counts', async () => {
    installWindow()
    const unreadTask = task()
    const unreadNotification = notification()
    api.items.mockResolvedValue(page([unreadTask, unreadNotification]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    const taskMutation = deferred<void>()
    api.markTaskRead.mockReturnValueOnce(taskMutation.promise)
    const markTask = workCenter.markRead(unreadTask)
    expect(workCenter.items.value[0]?.isRead).toBe(true)
    taskMutation.resolve()
    await markTask
    expect(api.markTaskRead).toHaveBeenCalledWith('task-1')

    api.items.mockResolvedValue(page([unreadTask, unreadNotification]))
    await workCenter.load()
    const notificationMutation = deferred<void>()
    api.markNotificationRead.mockReturnValueOnce(notificationMutation.promise)
    const markNotification = workCenter.markRead(unreadNotification)
    expect(workCenter.items.value[1]?.isRead).toBe(true)
    expect(workCenter.summary.value?.attentionCount).toBe(1)
    expect(workCenter.summary.value?.unreadNotificationCount).toBe(0)
    notificationMutation.resolve()
    await markNotification
    expect(api.markNotificationRead).toHaveBeenCalledWith('notification-1')

    await workCenter.markRead(task({ id: 'not-in-feed', isRead: true }))
    expect(api.markTaskRead).toHaveBeenLastCalledWith('not-in-feed')
  })

  it('rolls back optimistic read state and applies authorization cleanup on failure', async () => {
    installWindow()
    const unreadNotification = notification()
    api.items.mockResolvedValue(page([unreadNotification]))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    api.markNotificationRead.mockRejectedValueOnce(await createApiError(401, 'session expired'))
    await expect(workCenter.markRead(unreadNotification)).rejects.toThrow('session expired')

    expect(workCenter.items.value).toEqual([])
    expect(workCenter.summary.value).toBeNull()
    expect(workCenter.error.value).toBe('session expired')
  })

  it('optimistically dismisses notifications, clamps counts, and rolls back failures', async () => {
    installWindow()
    const unreadNotification = notification()
    api.items.mockResolvedValue(page([unreadNotification]))
    api.summary.mockResolvedValue(summary({
      attentionCount: 0,
      notificationCount: 0,
      unreadNotificationCount: 0,
    }))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()

    const dismissRequest = deferred<void>()
    api.dismiss.mockReturnValueOnce(dismissRequest.promise)
    const dismiss = workCenter.dismiss(unreadNotification)
    expect(workCenter.items.value).toEqual([])
    expect(workCenter.summary.value?.attentionCount).toBe(0)
    expect(workCenter.summary.value?.notificationCount).toBe(0)
    expect(workCenter.summary.value?.unreadNotificationCount).toBe(0)
    dismissRequest.resolve()
    await dismiss

    const readNotification = notification({ isRead: true })
    api.items.mockResolvedValue(page([readNotification]))
    await workCenter.load()
    api.dismiss.mockRejectedValueOnce(new Error('dismiss failed'))
    await expect(workCenter.dismiss(readNotification)).rejects.toThrow('dismiss failed')
    expect(workCenter.items.value).toEqual([readNotification])
    expect(workCenter.error.value).toBe('dismiss failed')
  })

  it('dismisses safely before a summary has been loaded', async () => {
    installWindow()
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    await workCenter.dismiss(notification({ isRead: true }))

    expect(api.dismiss).toHaveBeenCalledWith('notification-1')
    expect(workCenter.summary.value).toEqual(summary())
  })

  it('disposes a scoped feed session and aborts its active request', async () => {
    installWindow()
    const request = deferred<WorkCenterPage>()
    api.items.mockReturnValueOnce(request.promise)
    const { useWorkCenter } = await freshWorkCenter()
    const scope = effectScope()
    const workCenter = scope.run(() => useWorkCenter())!

    const load = workCenter.load({ tab: 'tasks' })
    const signal = api.items.mock.calls[0]?.[1] as AbortSignal
    scope.stop()

    expect(signal.aborted).toBe(true)
    request.resolve(page([task()]))
    await load
    expect(workCenter.items.value).toEqual([])
  })

  it('does not create a realtime connection during server rendering', async () => {
    const { useWorkCenter } = await freshWorkCenter()
    await expect(useWorkCenter().connectRealtime()).resolves.toBeUndefined()
    expect(signalr.builder.build).not.toHaveBeenCalled()
  })

  it('connects with safe transports and token branches and deduplicates concurrent starts', async () => {
    installWindow()
    environment.read.mockReturnValue('https://api.example/base')
    const startRequest = deferred<void>()
    signalr.connection.start.mockReturnValueOnce(startRequest.promise)
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    const first = workCenter.connectRealtime()
    const second = workCenter.connectRealtime()

    expect(signalr.builder.withUrl).toHaveBeenCalledWith(
      'https://api.example/hubs/work-center',
      expect.objectContaining({ transport: 3 }),
    )
    expect(signalr.builder.configureLogging).toHaveBeenCalledWith(4)
    expect(signalr.builder.withAutomaticReconnect).toHaveBeenCalledWith([0, 2_000, 10_000, 30_000])
    expect(signalr.builder.build).toHaveBeenCalledTimes(1)

    const options = signalr.builder.withUrl.mock.calls[0]?.[1] as {
      accessTokenFactory: () => Promise<string>
    }
    auth.getSnapshot.mockReturnValueOnce({
      initialized: false,
      authenticated: false,
      token: 'bootstrap-token',
    })
    await expect(options.accessTokenFactory()).resolves.toBe('bootstrap-token')
    auth.getSnapshot.mockReturnValueOnce({
      initialized: true,
      authenticated: false,
      token: undefined,
    })
    await expect(options.accessTokenFactory()).resolves.toBe('')
    auth.getSnapshot.mockReturnValueOnce({
      initialized: true,
      authenticated: true,
      token: 'stale',
    })
    await expect(options.accessTokenFactory()).resolves.toBe('fresh-token')
    auth.getAccessToken.mockResolvedValueOnce(null)
    await expect(options.accessTokenFactory()).resolves.toBe('')

    startRequest.resolve()
    await Promise.all([first, second])
    await workCenter.connectRealtime()
    expect(signalr.builder.build).toHaveBeenCalledTimes(1)
  })

  it('refreshes the shared summary without loading an inactive feed on realtime invalidation', async () => {
    installWindow()
    const { useWorkCenter } = await freshWorkCenter()
    const shellWorkCenter = useWorkCenter()
    await shellWorkCenter.connectRealtime()

    const changed = signalr.handlers.get('workCenterChanged')
    expect(changed).toBeTypeOf('function')
    changed?.(7 as never)

    await vi.waitFor(() => expect(api.summary).toHaveBeenCalledTimes(1))
    expect(api.items).not.toHaveBeenCalled()
    expect(shellWorkCenter.summary.value?.version).toBe(4)
  })

  it('contains rejected inactive-feed refreshes from realtime callbacks', async () => {
    installWindow()
    api.summary.mockRejectedValue(new Error('summary unavailable'))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.connectRealtime()

    signalr.handlers.get('workCenterChanged')?.(12 as never)
    await vi.waitFor(() => expect(api.summary).toHaveBeenCalledTimes(1))
    await flush()

    signalr.reconnectedHandler?.()
    await vi.waitFor(() => expect(api.summary).toHaveBeenCalledTimes(2))
    await flush()

    expect(workCenter.summary.value).toBeNull()
  })

  it('uses the current origin when no API base is configured and refetches on valid invalidations', async () => {
    installWindow('https://shell.example')
    api.items.mockResolvedValue(page([task()]))
    api.summary.mockResolvedValue(summary({ version: 5 }))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()
    await workCenter.load()
    await workCenter.connectRealtime()

    expect(signalr.builder.withUrl).toHaveBeenCalledWith(
      'https://shell.example/hubs/work-center',
      expect.any(Object),
    )

    const changed = signalr.handlers.get('workCenterChanged')
    expect(changed).toBeTypeOf('function')
    changed?.(0 as never)
    changed?.(Number.NaN as never)
    changed?.(7 as never)
    changed?.(6 as never)
    signalr.reconnectedHandler?.()
    await vi.waitFor(() => expect(workCenter.loading.value).toBe(false))

    expect(api.items.mock.calls.length).toBeGreaterThan(0)

    api.items.mockRejectedValueOnce(new Error('invalidation refresh failed'))
    changed?.(Number.NaN as never)
    signalr.reconnectedHandler?.()
    await vi.waitFor(() => expect(workCenter.error.value).toBe('invalidation refresh failed'))
  })

  it('recovers from close and failed starts without leaking stop failures or duplicate timers', async () => {
    vi.useFakeTimers()
    installWindow()
    signalr.connection.start
      .mockRejectedValueOnce(new Error('socket unavailable'))
      .mockResolvedValue(undefined)
    signalr.connection.stop.mockRejectedValueOnce(new Error('already stopped'))
    const { useWorkCenter } = await freshWorkCenter()
    const workCenter = useWorkCenter()

    await workCenter.connectRealtime()
    expect(signalr.connection.stop).toHaveBeenCalledTimes(1)

    signalr.closeHandler?.()
    signalr.closeHandler?.()
    await vi.advanceTimersByTimeAsync(30_000)
    await flush()

    expect(signalr.connection.start).toHaveBeenCalledTimes(2)
  })
})
