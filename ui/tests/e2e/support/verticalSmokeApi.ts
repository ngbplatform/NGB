import type { Page, Route } from '@playwright/test'

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

type VerticalSmokeProfile = {
  roleCode: string
  roleName: string
  mainMenu: {
    groups: VerticalMenuGroup[]
  }
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
