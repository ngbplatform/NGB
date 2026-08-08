import type { Page, Route } from '@playwright/test'
import type { NotificationPreference, WorkCenterItem } from '@ngbplatform/ui/contracts'

type VerticalMenuItem = {
  kind: string
  code: string
  label: string
  route: string
  icon?: string
  ordinal: number
}

type VerticalMenuGroup = {
  label: string
  ordinal: number
  icon?: string
  items: VerticalMenuItem[]
}

export type VerticalSmokeProfile = {
  roleCode: string
  roleName: string
  mainMenu: {
    groups: VerticalMenuGroup[]
  }
}

export type VerticalWorkCenterMock = {
  readonly taskTitle: string
  readonly notificationTitle: string
  getRequests: () => readonly string[]
  getMutations: () => readonly string[]
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}

function emptyReportResponse() {
  return {
    sheet: {
      columns: [],
      rows: [],
      meta: null,
      headerRows: [],
    },
    offset: 0,
    limit: 200,
    total: 0,
    hasMore: false,
    nextCursor: null,
    diagnostics: null,
  }
}

export async function mockVerticalSmokeApis(page: Page, profile: VerticalSmokeProfile): Promise<void> {
  await page.route('**/api/security/me/access', async (route) => {
    await fulfillJson(route, {
      userId: 'ngb-e2e-user',
      authSubject: 'ngb-e2e-user',
      isAuthenticated: true,
      isActive: true,
      isBootstrapAdmin: true,
      accessVersion: 1,
      roles: [
        {
          roleId: '11111111-1111-4111-8111-111111111111',
          code: profile.roleCode,
          name: profile.roleName,
          isSystem: true,
          isActive: true,
        },
      ],
      permissions: [],
    })
  })

  await page.route('**/api/main-menu', async (route) => {
    await fulfillJson(route, profile.mainMenu)
  })

  await page.route('**/api/report-definitions', async (route) => {
    await fulfillJson(route, [])
  })

  await page.route('**/api/reports/**/execute', async (route) => {
    await fulfillJson(route, emptyReportResponse())
  })

  await page.route('**/api/search/command-palette', async (route) => {
    await fulfillJson(route, {
      groups: [
        {
          code: 'go-to',
          label: 'Go To',
          items: profile.mainMenu.groups.flatMap((group) =>
            group.items.map((item) => ({
              key: `${item.kind}:${item.code}`,
              kind: item.kind,
              title: item.label,
              subtitle: group.label,
              icon: item.icon ?? group.icon ?? null,
              badge: item.kind,
              route: item.route,
              commandCode: null,
              status: null,
              openInNewTabSupported: true,
              score: 100,
            })),
          ),
        },
      ],
    })
  })
}

export async function mockVerticalWorkCenterApis(
  page: Page,
  profile: VerticalSmokeProfile,
  vertical: 'crm' | 'ab' | 'trade',
): Promise<VerticalWorkCenterMock> {
  await mockVerticalSmokeApis(page, profile)

  const document = profile.mainMenu.groups
    .flatMap((group) => group.items)
    .find((item) => item.kind === 'document')
  if (!document) throw new Error(`Missing document route for ${vertical} Work Center fixture.`)

  const requests: string[] = []
  const mutations: string[] = []
  let taskClaimed = false
  let taskSnoozed = false
  let notificationRead = false
  let notificationDismissed = false
  let taskEnabled = true

  const taskTitle = `${profile.roleName} follow-up`
  const notificationTitle = `${profile.roleName} update`
  const source = {
    resourceKind: 'Document',
    resourceCode: document.code,
    entityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    title: `${document.label} DEMO-001`,
    subtitle: profile.roleName,
  }

  const task = (): WorkCenterItem => ({
    id: '11111111-aaaa-4111-8111-111111111111',
    kind: 'Task',
    code: `${vertical}.demo.follow_up`,
    title: taskTitle,
    description: `Complete the next ${profile.roleName} workflow step.`,
    source,
    priority: 'High',
    severity: null,
    taskStatus: taskClaimed ? 'InProgress' : 'Open',
    sortAtUtc: '2026-08-08T12:00:00Z',
    dueAtUtc: '2026-08-07T12:00:00Z',
    isOverdue: true,
    isRead: false,
    snoozedUntilUtc: taskSnoozed ? '2099-08-09T12:00:00Z' : null,
    assignment: {
      assignedUserId: null,
      assignedRoleId: '11111111-1111-4111-8111-111111111111',
      claimedByUserId: taskClaimed ? 'ngb-e2e-user' : null,
      isRoleAssigned: true,
    },
    primaryActionCode: `${vertical}.demo.take_action`,
    target: null,
    version: taskClaimed || taskSnoozed ? 2 : 1,
  })

  const notification = (): WorkCenterItem => ({
    id: '22222222-bbbb-4222-8222-222222222222',
    kind: 'Notification',
    code: `${vertical}.demo.updated`,
    title: notificationTitle,
    description: `A ${profile.roleName} record changed.`,
    source,
    priority: null,
    severity: 'Information',
    taskStatus: null,
    sortAtUtc: '2026-08-08T12:05:00Z',
    dueAtUtc: null,
    isOverdue: false,
    isRead: notificationRead,
    snoozedUntilUtc: null,
    assignment: null,
    primaryActionCode: null,
    target: null,
    version: notificationRead ? 2 : 1,
  })

  const preferences = (): NotificationPreference[] => [
    {
      code: `${vertical}.demo.follow_up`,
      kind: 'Task',
      displayName: taskTitle,
      description: `Creates the ${profile.roleName} follow-up task.`,
      category: `${profile.roleName} Tasks`,
      channel: 'InApp',
      isEnabled: taskEnabled,
      defaultEnabled: true,
      userCanDisable: true,
      isMandatory: false,
    },
    {
      code: `${vertical}.demo.updated`,
      kind: 'Notification',
      displayName: notificationTitle,
      description: `Shows the ${profile.roleName} update.`,
      category: `${profile.roleName} Notifications`,
      channel: 'InApp',
      isEnabled: true,
      defaultEnabled: true,
      userCanDisable: true,
      isMandatory: false,
    },
  ]

  await page.route('**/api/work-center/summary**', async (route) => {
    requests.push(route.request().url())
    await fulfillJson(route, {
      attentionCount: (taskEnabled ? 1 : 0) + (!notificationDismissed && !notificationRead ? 1 : 0),
      openTaskCount: taskEnabled ? 1 : 0,
      overdueTaskCount: taskEnabled ? 1 : 0,
      notificationCount: notificationDismissed ? 0 : 1,
      unreadNotificationCount: !notificationDismissed && !notificationRead ? 1 : 0,
      version: mutations.length + 1,
    })
  })

  await page.route('**/api/work-center/items**', async (route) => {
    requests.push(route.request().url())
    const tab = new URL(route.request().url()).searchParams.get('tab')
    const items = [
      ...(taskEnabled ? [task()] : []),
      ...(!notificationDismissed ? [notification()] : []),
    ]
    await fulfillJson(route, {
      items: tab === 'tasks'
        ? items.filter((item) => item.kind === 'Task')
        : tab === 'notifications'
          ? items.filter((item) => item.kind === 'Notification')
          : items,
      nextCursor: null,
      limit: 30,
    })
  })

  await page.route('**/api/work-center/tasks/*/claim', async (route) => {
    taskClaimed = true
    mutations.push('claim')
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route('**/api/work-center/tasks/*/snooze', async (route) => {
    taskSnoozed = true
    mutations.push('snooze')
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route('**/api/work-center/tasks/*/read', async (route) => {
    mutations.push('task-read')
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route('**/api/work-center/notifications/*/read', async (route) => {
    notificationRead = true
    mutations.push('notification-read')
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route('**/api/work-center/notifications/*/dismiss', async (route) => {
    notificationDismissed = true
    mutations.push('dismiss')
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route('**/api/me/notification-preferences', async (route) => {
    if (route.request().method() === 'GET') {
      await fulfillJson(route, preferences())
      return
    }
    const payload = route.request().postDataJSON() as {
      preferences?: Array<{ code: string; isEnabled: boolean }>
    }
    const taskPreference = payload.preferences?.find((item) => item.code === `${vertical}.demo.follow_up`)
    if (taskPreference) taskEnabled = taskPreference.isEnabled
    mutations.push('preferences')
    await route.fulfill({ status: 204, body: '' })
  })

  return {
    taskTitle,
    notificationTitle,
    getRequests: () => [...requests],
    getMutations: () => [...mutations],
  }
}

export const CRM_SMOKE_PROFILE: VerticalSmokeProfile = {
  roleCode: 'crm-administrator',
  roleName: 'CRM Administrator',
  mainMenu: {
    groups: [
      {
        label: 'Home',
        ordinal: 0,
        icon: 'home',
        items: [
          { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
        ],
      },
      {
        label: 'Pipeline',
        ordinal: 10,
        icon: 'chart-no-axes-combined',
        items: [
          { kind: 'document', code: 'crm.lead_intake', label: 'Leads', route: '/documents/crm.lead_intake', icon: 'inbox', ordinal: 10 },
          { kind: 'document', code: 'crm.quote', label: 'Quotes', route: '/documents/crm.quote', icon: 'file-text', ordinal: 20 },
          { kind: 'report', code: 'crm.sales_pipeline', label: 'Sales Pipeline', route: '/reports/crm.sales_pipeline', icon: 'bar-chart', ordinal: 30 },
        ],
      },
    ],
  },
}

export const TRADE_SMOKE_PROFILE: VerticalSmokeProfile = {
  roleCode: 'trade-administrator',
  roleName: 'Trade Administrator',
  mainMenu: {
    groups: [
      {
        label: 'Home',
        ordinal: 0,
        icon: 'home',
        items: [
          { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
        ],
      },
      {
        label: 'Sales',
        ordinal: 10,
        icon: 'shopping-cart',
        items: [
          { kind: 'document', code: 'trd.sales_invoice', label: 'Sales Invoices', route: '/documents/trd.sales_invoice', icon: 'receipt', ordinal: 10 },
          { kind: 'report', code: 'trd.sales_by_customer', label: 'Sales by Customer', route: '/reports/trd.sales_by_customer', icon: 'bar-chart', ordinal: 20 },
        ],
      },
    ],
  },
}

export const AGENCY_BILLING_SMOKE_PROFILE: VerticalSmokeProfile = {
  roleCode: 'ab-administrator',
  roleName: 'Agency Billing Administrator',
  mainMenu: {
    groups: [
      {
        label: 'Home',
        ordinal: 0,
        icon: 'home',
        items: [
          { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
        ],
      },
      {
        label: 'Delivery',
        ordinal: 10,
        icon: 'calendar-check',
        items: [
          { kind: 'document', code: 'ab.timesheet', label: 'Timesheets', route: '/documents/ab.timesheet', icon: 'calendar-check', ordinal: 10 },
          { kind: 'document', code: 'ab.sales_invoice', label: 'Sales Invoices', route: '/documents/ab.sales_invoice', icon: 'receipt', ordinal: 20 },
          { kind: 'report', code: 'ab.ar_aging', label: 'AR Aging', route: '/reports/ab.ar_aging', icon: 'bar-chart', ordinal: 30 },
        ],
      },
    ],
  },
}
