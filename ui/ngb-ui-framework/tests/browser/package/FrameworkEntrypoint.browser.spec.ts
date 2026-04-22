import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import {
  NgbButton,
  NgbCommandPaletteDialog,
  NgbSiteShell,
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbLookup,
  configureNgbMetadata,
  configureNgbReporting,
  createDefaultNgbLookupConfig,
  createDefaultNgbReportingConfig,
  getConfiguredNgbCommandPalette,
  getConfiguredNgbEditor,
  getConfiguredNgbLookup,
  getConfiguredNgbMetadata,
  getConfiguredNgbReporting,
  useCommandPaletteStore,
} from 'ngb-ui-framework'

function createEditorFrameworkConfig() {
  return {
    loadDocumentById: vi.fn(async () => ({} as never)),
    loadDocumentEffects: vi.fn(async () => ({} as never)),
    loadDocumentGraph: vi.fn(async () => ({} as never)),
    loadEntityAuditLog: vi.fn(async () => ({} as never)),
  }
}

function createMetadataFrameworkConfig() {
  return {
    loadCatalogTypeMetadata: vi.fn(async () => ({} as never)),
    loadDocumentTypeMetadata: vi.fn(async () => ({} as never)),
  }
}

beforeEach(() => {
  window.localStorage.removeItem('ngb:test:package-entrypoint:palette')
})

test('mounts the public package entrypoint, configures framework singletons, and opens the exported palette dialog', async () => {
  await page.viewport(1440, 900)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div>Home</div>' } },
      { path: '/reports/occupancy', component: { template: '<div>Report</div>' } },
    ],
  })

  const commandPaletteConfig = {
    router,
    recentStorageKey: 'ngb:test:package-entrypoint:palette',
    loadReportItems: async () => [
      {
        key: 'report:occupancy',
        group: 'reports' as const,
        kind: 'report' as const,
        scope: 'reports' as const,
        title: 'Occupancy Summary',
        route: '/reports/occupancy',
        icon: 'bar-chart',
        defaultRank: 100,
      },
    ],
  }
  const lookupConfig = createDefaultNgbLookupConfig()
  const reportingConfig = createDefaultNgbReportingConfig()
  const metadataConfig = createMetadataFrameworkConfig()
  const editorConfig = createEditorFrameworkConfig()

  configureNgbCommandPalette(commandPaletteConfig)
  configureNgbLookup(lookupConfig)
  configureNgbReporting(reportingConfig)
  configureNgbMetadata(metadataConfig)
  configureNgbEditor(editorConfig)

  expect(getConfiguredNgbCommandPalette()).toBe(commandPaletteConfig)
  expect(getConfiguredNgbLookup()).toBe(lookupConfig)
  expect(getConfiguredNgbReporting()).toBe(reportingConfig)
  expect(getConfiguredNgbMetadata()).toBe(metadataConfig)
  expect(getConfiguredNgbEditor()).toBe(editorConfig)

  await router.push('/')
  await router.isReady()

  const EntrypointHarness = defineComponent({
    setup() {
      const palette = useCommandPaletteStore()
      const actionCount = ref(0)

      return () => h('div', [
        h(
          NgbSiteShell,
          {
            moduleTitle: 'Property Management',
            productTitle: 'NGB',
            pageTitle: 'Workspace',
            userName: 'Package Tester',
            userEmail: 'package.tester@example.com',
            userMeta: 'Administrator',
            userMetaIcon: 'shield-check',
            pinned: [],
            recent: [],
            nodes: [
              { id: 'dashboard', label: 'Dashboard', route: '/', icon: 'home' },
            ],
            settings: [],
            selectedId: 'dashboard',
            onNavigate: (_route: string) => undefined,
            onSelect: (_id: string, _route: string) => undefined,
            onOpenPalette: () => {
              palette.open()
            },
            onBack: () => undefined,
            onSignOut: () => undefined,
          },
          {
            default: () => h('div', { class: 'flex-1 min-h-0 overflow-auto p-6 space-y-4' }, [
              h(NgbButton, {
                variant: 'primary',
                onClick: () => {
                  actionCount.value += 1
                },
              }, () => 'Package action'),
              h('div', { 'data-testid': 'package-action-count' }, String(actionCount.value)),
            ]),
          },
        ),
        h(NgbCommandPaletteDialog),
      ])
    },
  })

  const view = await render(EntrypointHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  await expect.element(view.getByText('Package action', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Package action' }).click()
  await expect.element(view.getByTestId('package-action-count')).toHaveTextContent('1')

  await view.getByRole('button', { name: 'User' }).click()
  await expect.element(view.getByText('Package Tester', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: /Search pages, records, reports, or run a command/i }).click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  await expect.element(view.getByRole('combobox')).toBeVisible()
})
