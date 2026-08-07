import { page } from 'vitest/browser'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, nextTick, ref, type Ref } from 'vue'

import type {
  NotificationPreference,
  WorkCenterItem,
  WorkCenterSummary,
} from '../../../../src/ngb/work-center/types'
import { useWorkCenterInfiniteScroll } from '../../../../src/ngb/work-center/useWorkCenterInfiniteScroll'

const auth = vi.hoisted(() => ({
  snapshot: {
    initialized: true,
    authenticated: true,
  },
}))

const router = vi.hoisted(() => ({
  route: {
    query: {} as Record<string, unknown>,
  },
  push: vi.fn(async () => undefined),
  replace: vi.fn(async () => undefined),
  back: vi.fn(),
}))

const target = vi.hoisted(() => ({
  resolve: vi.fn(() => '/resolved-target'),
}))

const preferencesApi = vi.hoisted(() => ({
  get: vi.fn(),
  update: vi.fn(),
}))

type MockWorkCenter = {
  summary: Ref<WorkCenterSummary | null>
  items: Ref<WorkCenterItem[]>
  nextCursor: Ref<string | null>
  loading: Ref<boolean>
  loadingMore: Ref<boolean>
  error: Ref<string | null>
  loadMoreError: Ref<string | null>
  attentionCount: Ref<number>
  refreshSummary: ReturnType<typeof vi.fn>
  load: ReturnType<typeof vi.fn>
  refresh: ReturnType<typeof vi.fn>
  loadMore: ReturnType<typeof vi.fn>
  connectRealtime: ReturnType<typeof vi.fn>
  markRead: ReturnType<typeof vi.fn>
  dismiss: ReturnType<typeof vi.fn>
  claim: ReturnType<typeof vi.fn>
  snooze: ReturnType<typeof vi.fn>
}

const workCenter = vi.hoisted(() => ({
  current: null as MockWorkCenter | null,
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAuthSnapshot: () => auth.snapshot,
}))

vi.mock('vue-router', () => ({
  useRoute: () => router.route,
  useRouter: () => ({
    push: router.push,
    replace: router.replace,
    back: router.back,
  }),
}))

vi.mock('../../../../src/ngb/navigation/config', () => ({
  resolveNgbNavigationTarget: target.resolve,
  resolveNgbNavigationRoutes: () => ({
    workCenter: '/work-center',
    workCenterPreferences: '/settings/notifications',
  }),
}))

vi.mock('../../../../src/ngb/work-center/useWorkCenter', async () => {
  const { ref } = await vi.importActual<typeof import('vue')>('vue')
  workCenter.current = {
    summary: ref<WorkCenterSummary | null>(null),
    items: ref<WorkCenterItem[]>([]),
    nextCursor: ref<string | null>(null),
    loading: ref(false),
    loadingMore: ref(false),
    error: ref<string | null>(null),
    loadMoreError: ref<string | null>(null),
    attentionCount: ref(0),
    refreshSummary: vi.fn(async () => undefined),
    load: vi.fn(async () => undefined),
    refresh: vi.fn(async () => undefined),
    loadMore: vi.fn(async () => undefined),
    connectRealtime: vi.fn(async () => undefined),
    markRead: vi.fn(async () => undefined),
    dismiss: vi.fn(async () => undefined),
    claim: vi.fn(async () => undefined),
    snooze: vi.fn(async () => undefined),
  }
  return {
    useWorkCenter: () => workCenter.current,
    useWorkCenterPreferences: () => ({
      load: preferencesApi.get,
      save: preferencesApi.update,
    }),
  }
})

import NgbNotificationPreferencesPage from '../../../../src/ngb/work-center/NgbNotificationPreferencesPage.vue'
import NgbWorkCenterDrawer from '../../../../src/ngb/work-center/NgbWorkCenterDrawer.vue'
import NgbWorkCenterPage from '../../../../src/ngb/work-center/NgbWorkCenterPage.vue'

const state = workCenter.current!

function mockIntersectionObserver() {
  const previous = globalThis.IntersectionObserver
  const observerState = {
    callback: null as IntersectionObserverCallback | null,
    observed: [] as Element[],
    unobserved: [] as Element[],
  }

  class MockIntersectionObserver implements IntersectionObserver {
    readonly root = null
    readonly rootMargin = '320px 0px'
    readonly thresholds = [0]

    constructor(callback: IntersectionObserverCallback) {
      observerState.callback = callback
    }

    disconnect() {}
    observe(target: Element) {
      observerState.observed.push(target)
    }
    takeRecords(): IntersectionObserverEntry[] {
      return []
    }
    unobserve(target: Element) {
      observerState.unobserved.push(target)
    }
  }

  Object.defineProperty(globalThis, 'IntersectionObserver', {
    configurable: true,
    value: MockIntersectionObserver,
  })

  return {
    state: observerState,
    intersectLastObserved() {
      const target = observerState.observed.at(-1)
      if (!target) return
      observerState.callback?.([{
        isIntersecting: true,
        target,
      } as IntersectionObserverEntry], {} as IntersectionObserver)
    },
    restore() {
      Object.defineProperty(globalThis, 'IntersectionObserver', {
        configurable: true,
        value: previous,
      })
    },
  }
}

let intersectionObserver = mockIntersectionObserver()

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
      entityId: 'payment/1',
      title: 'Payment 1',
      subtitle: 'Customer A',
    },
    priority: 'High',
    taskStatus: 'Open',
    sortAtUtc: '2026-07-26T15:00:00.000Z',
    isOverdue: false,
    isRead: false,
    assignment: {
      assignedRoleId: 'pm-accountant',
      claimedByUserId: null,
      isRoleAssigned: true,
    },
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
    description: null,
    priority: null,
    severity: 'Warning',
    taskStatus: null,
    assignment: null,
    ...overrides,
  })
}

const baseSummary: WorkCenterSummary = {
  attentionCount: 4,
  openTaskCount: 2,
  overdueTaskCount: 1,
  notificationCount: 3,
  unreadNotificationCount: 2,
  version: 8,
}

const preference = (
  overrides: Partial<NotificationPreference> = {},
): NotificationPreference => ({
  code: 'crm.qualify_lead',
  kind: 'Task',
  displayName: 'Qualify lead',
  description: 'Creates a task when a new lead needs qualification.',
  category: 'CRM Tasks',
  channel: 'InApp',
  isEnabled: true,
  defaultEnabled: true,
  userCanDisable: true,
  isMandatory: false,
  ...overrides,
})

beforeEach(() => {
  intersectionObserver.restore()
  intersectionObserver = mockIntersectionObserver()
  vi.clearAllMocks()
  auth.snapshot.initialized = true
  auth.snapshot.authenticated = true
  router.route.query = {}
  target.resolve.mockImplementation((navigationTarget: { code?: string; parameters?: Record<string, unknown> }) => {
    if (navigationTarget.code === 'document.editor') {
      const documentType = encodeURIComponent(String(navigationTarget.parameters?.documentType ?? ''))
      const documentId = encodeURIComponent(String(navigationTarget.parameters?.documentId ?? ''))
      return `/documents/${documentType}/${documentId}`
    }
    return '/resolved-target'
  })
  state.summary.value = null
  state.items.value = []
  state.nextCursor.value = null
  state.loading.value = false
  state.loadingMore.value = false
  state.error.value = null
  state.loadMoreError.value = null
  state.attentionCount.value = 0
  state.load.mockResolvedValue(undefined)
  state.markRead.mockResolvedValue(undefined)
  state.dismiss.mockResolvedValue(undefined)
  state.claim.mockResolvedValue(undefined)
  state.snooze.mockResolvedValue(undefined)
  state.connectRealtime.mockResolvedValue(undefined)
  preferencesApi.get.mockResolvedValue([])
  preferencesApi.update.mockResolvedValue(undefined)
})

afterEach(() => {
  intersectionObserver.restore()
})

test('drawer renders loading, error, empty and actionable feed states', async () => {
  state.loading.value = true
  const view = await render(NgbWorkCenterDrawer)
  await expect.element(view.getByText('Loading Work Center…')).toBeVisible()
  await vi.waitFor(() => expect(state.load).toHaveBeenCalledWith({
    tab: 'attention',
    limit: 20,
    vertical: null,
  }))

  state.loading.value = false
  state.error.value = 'Temporary gateway failure'
  await nextTick()
  await expect.element(view.getByText('Work Center is temporarily unavailable')).toBeVisible()
  state.load.mockRejectedValueOnce(new Error('still unavailable'))
  await view.getByRole('button', { name: 'Retry' }).click()

  auth.snapshot.initialized = false
  await view.getByRole('button', { name: 'Retry' }).click()
  auth.snapshot.initialized = true
  auth.snapshot.authenticated = false
  await view.getByRole('button', { name: 'Retry' }).click()
  auth.snapshot.authenticated = true

  state.error.value = null
  await nextTick()
  await expect.element(view.getByText('You’re all caught up')).toBeVisible()

  state.items.value = [
    task({
      target: {
        kind: 'Route',
        code: 'document.effects',
        parameters: {},
      },
    }),
    task({
      id: 'task-claimed',
      title: 'Already claimed',
      description: null,
      isRead: true,
      assignment: {
        assignedRoleId: 'pm-accountant',
        claimedByUserId: 'user-1',
        isRoleAssigned: true,
      },
    }),
    notification(),
    task({
      id: 'snoozed-task',
      title: 'Snoozed task',
      isRead: true,
      snoozedUntilUtc: '2099-07-30T15:00:00.000Z',
      assignment: {
        assignedRoleId: 'pm-accountant',
        claimedByUserId: 'user-1',
        isRoleAssigned: true,
      },
    }),
  ]
  await nextTick()

  await expect.element(view.getByText('Apply payment')).toBeVisible()
  await expect.element(view.getByText('Already claimed')).toBeVisible()
  state.claim.mockRejectedValueOnce(new Error('claim race'))
  state.dismiss.mockRejectedValueOnce(new Error('already dismissed'))
  await view.getByRole('button', { name: 'Assign to me', exact: true }).click()
  await view.getByRole('button', { name: 'Dismiss' }).click()
  expect(state.claim).toHaveBeenCalledWith(state.items.value[0])
  expect(state.dismiss).toHaveBeenCalledWith(state.items.value[2])

  state.snooze.mockRejectedValueOnce(new Error('show-now race'))
  await view.getByRole('button', { name: 'Show now' }).click()
  expect(state.snooze).toHaveBeenCalledWith(
    state.items.value[3],
    expect.stringMatching(/Z$/),
  )

  state.markRead.mockRejectedValueOnce(new Error('read race'))
  await view.getByRole('button', { name: /^Apply payment/ }).click()
  expect(state.markRead).toHaveBeenCalledWith(state.items.value[0])
  expect(target.resolve).toHaveBeenCalled()
  expect(router.push).toHaveBeenCalledWith('/resolved-target')

  await view.getByRole('tab', { name: 'Notifications' }).click()
  await vi.waitFor(() => expect(state.load).toHaveBeenCalledWith({
    tab: 'notifications',
    limit: 20,
    vertical: null,
  }))
  await view.getByRole('button', { name: 'View all' }).click()
  expect(router.push).toHaveBeenCalledWith({
    path: '/work-center',
    query: { tab: 'notifications' },
  })

  state.loadingMore.value = true
  await nextTick()
  await expect.element(view.getByText('Loading more…')).toBeVisible()
  state.loadingMore.value = false
  state.loadMoreError.value = 'next page failed'
  await nextTick()
  await view.getByRole('button', { name: 'Couldn’t load more. Retry' }).click()
  expect(state.loadMore).toHaveBeenCalled()
})

test('drawer keeps count-free tabs, includes Completed, and loads the next page on intersection', async () => {
  state.summary.value = baseSummary
  state.items.value = [task({ taskStatus: 'Completed' })]
  state.nextCursor.value = 'drawer-cursor-2'

  const view = await render(NgbWorkCenterDrawer)
  view.container.style.width = '550px'
  await nextTick()

  await expect.element(view.getByRole('tab', { name: 'Needs Attention' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Tasks' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Notifications' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Completed' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Assign to me' })).not.toBeInTheDocument()

  const tabElements: HTMLButtonElement[] = []
  for (const name of ['Needs Attention', 'Tasks', 'Notifications', 'Completed']) {
    const tab = view.getByRole('tab', { name }).element() as HTMLButtonElement
    tabElements.push(tab)
    const style = getComputedStyle(tab)
    expect(style.height).toBe('32px')
    expect(style.fontSize).toBe('14px')
    expect(style.whiteSpace).toBe('nowrap')
    expect(style.flexGrow).toBe('1')
    expect(tab.scrollHeight).toBeLessThanOrEqual(tab.clientHeight)
    expect(tab.scrollWidth).toBeLessThanOrEqual(tab.clientWidth)
  }

  const tabList = view.getByRole('tablist', { name: 'Work Center views' }).element() as HTMLElement
  const tabListStyle = getComputedStyle(tabList)
  const availableTabWidth = tabList.clientWidth
    - Number.parseFloat(tabListStyle.paddingLeft)
    - Number.parseFloat(tabListStyle.paddingRight)
  const renderedTabWidth = tabElements.reduce(
    (total, element) => total + element.getBoundingClientRect().width,
    0,
  )
  expect(Math.abs(availableTabWidth - renderedTabWidth)).toBeLessThanOrEqual(1)

  state.loadMore.mockRejectedValueOnce(new Error('cursor expired'))
  intersectionObserver.intersectLastObserved()
  await vi.waitFor(() => expect(state.loadMore).toHaveBeenCalledTimes(1))
})

test('infinite scroll handles unavailable observers and sentinel replacement safely', async () => {
  intersectionObserver.restore()
  Object.defineProperty(globalThis, 'IntersectionObserver', {
    configurable: true,
    value: undefined,
  })

  const nextCursor = ref<string | null>('cursor-2')
  const loading = ref(false)
  const loadingMore = ref(false)
  const loadMoreError = ref<string | null>(null)
  const loadMore = vi.fn(async () => undefined)
  const showSentinel = ref(true)
  const Harness = defineComponent({
    setup() {
      const { sentinel } = useWorkCenterInfiniteScroll({
        nextCursor,
        loading,
        loadingMore,
        loadMoreError,
        loadMore,
      })
      return () => showSentinel.value
        ? h('div', {
            ref: (element: Element | null) => { sentinel.value = element as HTMLElement | null },
          })
        : h('span')
    },
  })

  let view = await render(Harness)
  await nextTick()
  showSentinel.value = false
  await nextTick()
  view.unmount()

  intersectionObserver = mockIntersectionObserver()
  showSentinel.value = true
  view = await render(Harness)
  await nextTick()
  showSentinel.value = false
  await nextTick()
  expect(intersectionObserver.state.unobserved).toHaveLength(1)
  view.unmount()

  const NoSentinelHarness = defineComponent({
    setup() {
      useWorkCenterInfiniteScroll({
        nextCursor,
        loading,
        loadingMore,
        loadMoreError,
        loadMore,
      })
      return () => h('div')
    },
  })
  view = await render(NoSentinelHarness)
  view.unmount()
})

test('drawer opens document fallbacks and safely ignores non-document sources', async () => {
  state.items.value = [
    task({ isRead: true, target: null }),
    task({
      id: 'party-task',
      title: 'Review party',
      target: null,
      source: {
        resourceKind: 'Party',
        resourceCode: 'party',
        entityId: 'party-1',
        title: 'Party 1',
      },
    }),
  ]
  const view = await render(NgbWorkCenterDrawer)

  await view.getByRole('button', { name: /^Apply payment/ }).click()
  expect(state.markRead).not.toHaveBeenCalled()
  expect(router.push).toHaveBeenCalledWith('/documents/pm.receivable-payment/payment%2F1')

  router.push.mockClear()
  await view.getByRole('button', { name: /^Review party/ }).click()
  expect(router.push).not.toHaveBeenCalled()
})

test('full Work Center applies filters, mutates items, routes targets, and paginates', async () => {
  state.summary.value = baseSummary
  state.items.value = [
    task({
      target: {
        kind: 'Route',
        code: 'document.effects',
        parameters: {},
      },
      primaryActionCode: 'pm.payment.apply',
    }),
    notification(),
    task({
      id: 'snoozed-task',
      title: 'Snoozed task',
      isRead: true,
      snoozedUntilUtc: '2099-07-30T15:00:00.000Z',
      assignment: {
        assignedRoleId: 'pm-accountant',
        claimedByUserId: 'user-1',
        isRoleAssigned: true,
      },
    }),
    task({
      id: 'party-task',
      title: 'Review party',
      isRead: true,
      description: null,
      taskStatus: 'Completed',
      assignment: null,
      target: null,
      source: {
        resourceKind: 'Party',
        resourceCode: 'party',
        entityId: 'party-1',
        title: 'Party 1',
        subtitle: null,
      },
    }),
  ]
  state.nextCursor.value = 'cursor-2'
  const view = await render(NgbWorkCenterPage, { props: { vertical: 'pm' } })

  await vi.waitFor(() => {
    expect(state.load).toHaveBeenCalled()
    expect(state.connectRealtime).toHaveBeenCalled()
  })

  await expect.element(view.getByText('2 open tasks', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Needs Attention (4)' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Tasks (2)' })).toBeVisible()
  await expect.element(view.getByRole('tab', { name: 'Notifications (3)' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'More actions for Review party' }))
    .not.toBeInTheDocument()
  await view.getByRole('button', { name: 'Priority' }).click()
  await page.getByRole('option', { name: 'Critical' }).click()
  await expect.element(view.getByRole('button', { name: 'Priority' })).toHaveTextContent('Priority: Critical')
  await view.getByRole('button', { name: 'Severity' }).click()
  await page.getByRole('option', { name: 'Warning' }).click()
  await expect.element(view.getByRole('button', { name: 'Severity' })).toHaveTextContent('Severity: Warning')
  await view.getByLabelText('Overdue').click()
  await view.getByRole('checkbox', { name: 'Unread' }).click()
  await vi.waitFor(() => {
    expect(state.load).toHaveBeenLastCalledWith({
      tab: 'attention',
      vertical: 'pm',
      priority: 'Critical',
      severity: 'Warning',
      overdue: true,
      unread: true,
    })
  })
  await view.getByRole('button', { name: 'Priority' }).click()
  await page.getByRole('option', { name: 'All', exact: true }).click()
  await view.getByRole('button', { name: 'Severity' }).click()
  await page.getByRole('option', { name: 'All', exact: true }).click()
  await vi.waitFor(() => expect(state.load).toHaveBeenLastCalledWith({
    tab: 'attention',
    vertical: 'pm',
    priority: null,
    severity: null,
    overdue: true,
    unread: true,
  }))
  const buttonLabels = Array.from(document.querySelectorAll('button'))
    .map((button) => button.textContent?.trim())
  expect(buttonLabels).not.toContain('Apply')
  expect(buttonLabels).not.toContain('Clear')

  const priorityButton = document.querySelector<HTMLButtonElement>('button[aria-label="Priority"]')
  const overdueCheckbox = document.querySelector<HTMLInputElement>('input[type="checkbox"]')
  const workCenterTabs = document.querySelector('[data-testid="work-center-tabs"]')
  expect(priorityButton?.getBoundingClientRect().height).toBe(26)
  expect(overdueCheckbox?.closest('label')?.className).not.toContain('border')
  expect(Array.from(workCenterTabs?.querySelectorAll('[role="tab"]') ?? [])
    .every((item) => !item.className.includes('flex-1'))).toBe(true)

  await view.getByRole('tab', { name: 'Notifications' }).click()
  expect(router.replace).toHaveBeenCalledWith({ query: { tab: 'notifications' } })

  state.claim.mockRejectedValueOnce(new Error('claim race'))
  state.snooze.mockRejectedValueOnce(new Error('snooze race'))
  state.dismiss.mockRejectedValueOnce(new Error('dismiss race'))
  await view.getByRole('button', { name: 'More actions for Apply payment' }).click()
  await page.getByRole('menuitem', { name: 'Assign to me' }).click()
  await view.getByRole('button', { name: 'More actions for Apply payment' }).click()
  await page.getByRole('menuitem', { name: 'Snooze 1 day' }).click()
  await expect.element(view.getByText(/Snoozed until/)).toBeVisible()
  state.snooze.mockRejectedValueOnce(new Error('show-now race'))
  await view.getByRole('button', { name: 'More actions for Snoozed task' }).click()
  await page.getByRole('menuitem', { name: 'Show now' }).click()
  await view.getByRole('button', { name: 'More actions for Payment posted' }).click()
  await page.getByRole('menuitem', { name: 'Dismiss' }).click()
  expect(state.claim).toHaveBeenCalledWith(state.items.value[0])
  expect(state.snooze).toHaveBeenNthCalledWith(
    1,
    state.items.value[0],
    expect.stringMatching(/Z$/),
  )
  expect(state.snooze).toHaveBeenNthCalledWith(
    2,
    state.items.value[2],
    expect.stringMatching(/Z$/),
  )
  expect(state.dismiss).toHaveBeenCalledWith(state.items.value[1])

  state.markRead.mockRejectedValueOnce(new Error('read race'))
  await view.getByRole('button', { name: 'More actions for Apply payment' }).click()
  await page.getByRole('menuitem', { name: 'Take action' }).click()
  expect(state.markRead).toHaveBeenCalledWith(state.items.value[0])
  expect(router.push).toHaveBeenCalledWith('/resolved-target')

  intersectionObserver.intersectLastObserved()
  await vi.waitFor(() => expect(state.loadMore).toHaveBeenCalled())
  state.loadingMore.value = true
  await nextTick()
  await expect.element(view.getByText('Loading more…')).toBeVisible()
  state.loadingMore.value = false
  state.loadMoreError.value = 'cursor expired'
  await nextTick()
  await view.getByRole('button', { name: 'Couldn’t load more. Retry' }).click()

  await view.getByRole('button', { name: 'Refresh' }).click()
  expect(state.load).toHaveBeenCalled()
  await view.getByRole('button', { name: 'Back' }).click()
  expect(router.back).toHaveBeenCalled()
})

test('full Work Center covers fallback routing and loading/error/empty views', async () => {
  auth.snapshot.initialized = false
  auth.snapshot.authenticated = true
  state.loading.value = true
  const view = await render(NgbWorkCenterPage)
  await vi.waitFor(() => {
    expect(state.load).toHaveBeenCalled()
    expect(state.connectRealtime).toHaveBeenCalled()
  })
  await expect.element(view.getByRole('status')).toHaveTextContent('Loading…')

  state.loading.value = false
  state.error.value = 'Read model unavailable'
  await nextTick()
  state.load.mockRejectedValueOnce(new Error('retry failed'))
  await view.getByRole('button', { name: 'Retry' }).click()

  state.error.value = null
  await nextTick()
  await expect.element(view.getByText('You’re all caught up')).toBeVisible()

  state.items.value = [
    task({ isRead: true, target: null }),
    task({
      id: 'party-task',
      title: 'Review party',
      isRead: true,
      target: null,
      source: {
        resourceKind: 'Party',
        resourceCode: 'party',
        entityId: 'party-1',
        title: 'Party 1',
      },
    }),
  ]
  await nextTick()
  await view.getByRole('button', { name: /^Apply payment/ }).click()
  expect(router.push).toHaveBeenCalledWith('/documents/pm.receivable-payment/payment%2F1')
  router.push.mockClear()
  await view.getByRole('button', { name: /^Review party/ }).click()
  expect(router.push).not.toHaveBeenCalled()
})

test('full Work Center accepts a valid initial tab and rejects an unknown one', async () => {
  router.route.query = { tab: 'completed', keep: 'yes' }
  let view = await render(NgbWorkCenterPage)
  await expect.element(view.getByRole('tab', { name: 'Completed' })).toHaveAttribute('aria-selected', 'true')
  view.unmount()

  router.route.query = { tab: 'unknown' }
  view = await render(NgbWorkCenterPage)
  await expect.element(view.getByRole('tab', { name: 'Needs Attention' })).toHaveAttribute('aria-selected', 'true')
})

test('Work Center preferences keep task and notification choices separate and persist writable values', async () => {
  let resolveLoad!: (preferences: NotificationPreference[]) => void
  const initialLoad = new Promise<NotificationPreference[]>((resolve) => {
    resolveLoad = resolve
  })
  preferencesApi.get
    .mockReturnValueOnce(initialLoad)
    .mockResolvedValueOnce([
      preference({ isEnabled: false }),
      preference({
        code: 'ngb.work_center.required',
        kind: 'Notification',
        displayName: 'Security access changes',
        category: 'Platform Notifications',
        description: undefined,
        userCanDisable: false,
        isMandatory: true,
      }),
    ])
  const view = await render(NgbNotificationPreferencesPage)
  await expect.element(view.getByText('Loading…')).toBeVisible()
  resolveLoad([
      preference(),
      preference({
        code: 'ngb.work_center.required',
        kind: 'Notification',
        displayName: 'Security access changes',
        category: 'Platform Notifications',
        description: undefined,
        userCanDisable: false,
        isMandatory: true,
      }),
    ])

  await vi.waitFor(() => expect(preferencesApi.get).toHaveBeenCalledTimes(1))
  await expect.element(view.getByText('CRM Tasks', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Platform Notifications', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Required notification')).toBeVisible()
  await expect.element(view.getByText(
    'Creates a task when a new lead needs qualification.',
  ).first()).toBeVisible()
  await expect.element(view.getByRole('heading', { name: 'Work Center preferences' })).toBeVisible()
  await expect.element(view.getByText(
    'Choose which tasks and informational notifications appear in your Work Center.',
  )).toBeVisible()

  const checkboxes = document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')
  expect(checkboxes).toHaveLength(2)
  expect(checkboxes[1]?.disabled).toBe(true)
  checkboxes[0]?.click()
  let resolveSave!: () => void
  preferencesApi.update.mockReturnValueOnce(new Promise<void>((resolve) => {
    resolveSave = resolve
  }))
  await view.getByRole('button', { name: 'Save preferences' }).click()
  await expect.element(view.getByRole('button', { name: 'Saving…' })).toBeDisabled()
  resolveSave()

  await vi.waitFor(() => expect(preferencesApi.update).toHaveBeenCalledWith([
    {
      code: 'crm.qualify_lead',
      channel: 'InApp',
      isEnabled: false,
    },
    {
      code: 'ngb.work_center.required',
      channel: 'InApp',
      isEnabled: true,
    },
  ]))
  expect(preferencesApi.get).toHaveBeenCalledTimes(2)
})

test('Work Center preferences expose deterministic load and save errors', async () => {
  preferencesApi.get.mockRejectedValueOnce(new Error('preferences offline'))
  const view = await render(NgbNotificationPreferencesPage)
  await expect.element(view.getByText('preferences offline')).toBeVisible()

  preferencesApi.get.mockResolvedValueOnce([preference()])
  await render(NgbNotificationPreferencesPage)
  await vi.waitFor(() => expect(preferencesApi.get).toHaveBeenCalledTimes(2))
  preferencesApi.update.mockRejectedValueOnce('gateway failure')
  const saveButtons = page.getByRole('button', { name: 'Save preferences' })
  await saveButtons.last().click()
  await expect.element(page.getByText('Unable to update Work Center preferences.').last()).toBeVisible()
})

test('Work Center preferences sort same-kind definitions and show the default description', async () => {
  preferencesApi.get.mockResolvedValueOnce([
    preference({
      code: 'crm.contract_renewed',
      kind: 'Notification',
      displayName: 'Contract renewed',
      category: 'CRM Notifications',
    }),
    preference({
      code: 'crm.convert_lead',
      displayName: 'Convert lead',
      description: undefined,
    }),
    preference({
      code: 'crm.qualify_lead',
      displayName: 'Qualify lead',
    }),
    preference({
      code: 'pm.apply_payment',
      displayName: 'Apply payment',
      category: 'Property Management Tasks',
    }),
  ])

  const view = await render(NgbNotificationPreferencesPage)

  await expect.element(view.getByText('Enabled in Work Center')).toBeVisible()
  const headings = Array.from(view.container.querySelectorAll('section > div'))
    .map((element) => element.textContent?.trim())
  expect(headings).toEqual(['CRM Tasks', 'Property Management Tasks', 'CRM Notifications'])
  const crmLabels = Array.from(view.container.querySelectorAll('section:first-of-type label'))
    .map((element) => element.textContent?.trim())
  expect(crmLabels[0]).toContain('Convert lead')
  expect(crmLabels[1]).toContain('Qualify lead')
})
