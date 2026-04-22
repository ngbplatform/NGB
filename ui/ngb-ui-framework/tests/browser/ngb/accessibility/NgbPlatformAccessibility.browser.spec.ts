import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'
import { ReportRowKind, type ReportSheetDto } from '../../../../src/ngb/reporting/types'

const shellNodes = [
  { id: 'dashboard', label: 'Dashboard', route: '/dashboard', icon: 'home' },
  {
    id: 'receivables',
    label: 'Receivables',
    icon: 'coins',
    children: [
      { id: 'payments', label: 'Payments', route: '/receivables/payments', icon: 'credit-card' },
    ],
  },
]

const shellSettings = [
  {
    label: 'Workspace',
    items: [
      { label: 'Preferences', route: '/settings/preferences', icon: 'settings', description: 'Workspace defaults' },
    ],
  },
]

const SiteShellAccessibilityHarness = defineComponent({
  setup() {
    return () => h(
      NgbSiteShell,
      {
        moduleTitle: 'Property Management',
        productTitle: 'NGB',
        userName: 'Alex Carter',
        userEmail: 'alex.carter@example.com',
        userMeta: 'Admin access',
        userMetaIcon: 'shield-check',
        unreadNotifications: 3,
        pinned: [],
        recent: [],
        nodes: shellNodes,
        settings: shellSettings,
        selectedId: 'dashboard',
      },
      {
        default: () => h('div', { class: 'flex-1 min-h-0 overflow-auto p-4' }, 'Workspace body'),
      },
    )
  },
})

function reportSheet(): ReportSheetDto {
  return {
    columns: [
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'units', title: 'Units', dataType: 'number' },
    ],
    headerRows: [
      {
        rowKind: ReportRowKind.Header,
        cells: [
          {
            display: 'Property Drilldown',
            value: 'Property Drilldown',
            rowSpan: 2,
            action: {
              kind: 'open_report',
              report: {
                reportCode: 'pm.property.detail',
                parameters: {
                  as_of_utc: '2026-04-08',
                },
                filters: null,
              },
            },
          },
          {
            display: 'Snapshot',
            value: 'Snapshot',
            colSpan: 1,
          },
        ],
      },
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Units', value: 'Units' },
        ],
      },
    ],
    rows: [
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          {
            display: 'Riverfront Tower',
            value: 'Riverfront Tower',
            valueType: 'string',
          },
          {
            display: '24',
            value: 24,
            valueType: 'decimal',
          },
        ],
      },
    ],
  }
}

async function renderReportSheetHarness() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div>Home</div>' } },
      { path: '/reports/:reportCode', component: { template: '<div>Report target</div>' } },
    ],
  })

  await router.push('/')
  await router.isReady()

  return await render(defineComponent({
    setup() {
      return () => h('div', {
        style: 'width: 960px; max-width: 960px; height: 720px; display: flex; min-width: 0; min-height: 0; overflow: hidden;',
      }, [
        h(NgbReportSheet, {
          sheet: reportSheet(),
          rowNoun: 'property',
        }),
      ])
    },
  }), {
    global: {
      plugins: [router],
    },
  })
}

test('exposes named shell controls and labeled drawer dialogs for platform chrome', async () => {
  await page.viewport(375, 800)

  const view = await render(SiteShellAccessibilityHarness)

  await expect.element(view.getByRole('button', { name: 'Main menu' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Notifications' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Help' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Settings' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Switch to dark mode' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: /Search pages, records, reports, or run a command/i })).toBeVisible()

  await view.getByRole('button', { name: 'Main menu' }).click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  expect(view.getByRole('dialog', { name: 'Main menu' }).element()).toBeTruthy()

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
  await expect.poll(() => document.querySelector('[data-testid="drawer-panel"]')).toBeNull()

  await view.getByRole('button', { name: 'Settings' }).click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  expect(view.getByRole('dialog', { name: 'Settings' }).element()).toBeTruthy()
  await expect.element(view.getByText('Preferences', { exact: true })).toBeVisible()
})

test('exposes report tables, column headers, and drilldown buttons through native semantics', async () => {
  await page.viewport(960, 800)

  const view = await renderReportSheetHarness()

  await expect.element(view.getByRole('table')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Property Drilldown' })).toBeVisible()
  expect(document.querySelectorAll('th')).toHaveLength(3)
  expect(Array.from(document.querySelectorAll('th')).map((entry) => entry.textContent?.trim())).toEqual(
    expect.arrayContaining(['Property Drilldown', 'Snapshot', 'Units']),
  )
  expect(Array.from(document.querySelectorAll('td')).map((entry) => entry.textContent?.trim())).toEqual(
    expect.arrayContaining(['Riverfront Tower', '24.00']),
  )
  await expect.element(view.getByText('24.00', { exact: true })).toBeVisible()
})
