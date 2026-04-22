import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { RouterView } from 'vue-router'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  buildChartOfAccountsPath: vi.fn(() => '/admin/chart-of-accounts'),
}))

vi.mock('ngb-ui-framework', async () => {
  const { defineComponent } = await import('vue')

  const stub = (name: string) =>
    defineComponent({
      props: {
        backTarget: {
          type: String,
          default: '',
        },
      },
      template: `<div data-testid="${name}">{{ backTarget }}</div>`,
    })

  return {
    buildChartOfAccountsPath: mocks.buildChartOfAccountsPath,
    createAuthGuard: () => (_to: unknown, _from: unknown, next: (value?: unknown) => void) => next(),
    getCatalogPage: vi.fn(async () => ({ items: [], total: 0 })),
    getDocumentPage: vi.fn(async () => ({ items: [], total: 0 })),
    lookupHintFromSource: (lookup?: unknown | null) => lookup ?? null,
    ngbRouteAliasRedirectRoutes: [],
    NgbAccountingPeriodClosingPage: stub('period-closing-page'),
    NgbChartOfAccountsPage: stub('chart-of-accounts-page'),
    NgbDocumentEffectsPage: stub('document-effects-page'),
    NgbDocumentFlowPage: stub('document-flow-page'),
    NgbDocumentPrintPage: stub('document-print-page'),
    NgbGeneralJournalEntryEditPage: stub('general-journal-edit-page'),
    NgbGeneralJournalEntryListPage: stub('general-journal-list-page'),
    NgbMetadataCatalogEditPage: stub('catalog-edit-page'),
    NgbMetadataCatalogListPage: stub('catalog-list-page'),
    NgbMetadataDocumentEditPage: stub('document-edit-page'),
    NgbMetadataDocumentListPage: stub('document-list-page'),
    NgbReportPage: stub('report-page'),
    useAuthStore: () => ({}),
  }
})

vi.mock('../../../src/pages/HomePage.vue', async () => {
  const { defineComponent } = await import('vue')
  return {
    default: defineComponent({
      template: '<div data-testid="home-page">Home Page</div>',
    }),
  }
})

vi.mock('../../../src/pages/AccountingPolicySettingsPage.vue', async () => {
  const { defineComponent } = await import('vue')
  return {
    default: defineComponent({
      template: '<div data-testid="accounting-policy-page">Accounting Policy</div>',
    }),
  }
})

vi.mock('../../../src/editor/AgencyBillingEntityEditor.vue', async () => {
  const { defineComponent } = await import('vue')
  return {
    default: defineComponent({
      template: '<div data-testid="agency-billing-entity-editor-stub"></div>',
    }),
  }
})

import { router } from '../../../src/router/router'

describe('agency billing router browser integration', () => {
  beforeEach(async () => {
    await router.replace('/home')
    await router.isReady()
  })

  afterEach(async () => {
    await router.replace('/home')
    await nextTick()
  })

  async function renderAt(path: string) {
    await router.replace(path)
    await nextTick()
    return render(RouterView, {
      global: {
        plugins: [router],
      },
    })
  }

  it('redirects the root route to the dashboard', async () => {
    const screen = await renderAt('/')

    await expect.element(screen.getByTestId('home-page')).toBeInTheDocument()
    expect(router.currentRoute.value.fullPath).toBe('/home')
  })

  it('renders the accounting policy singleton page', async () => {
    const screen = await renderAt('/catalogs/ab.accounting_policy')

    await expect.element(screen.getByTestId('accounting-policy-page')).toBeInTheDocument()
  })

  it('redirects accounting policy create route back to the singleton page', async () => {
    const screen = await renderAt('/catalogs/ab.accounting_policy/new')

    await expect.element(screen.getByTestId('accounting-policy-page')).toBeInTheDocument()
    expect(router.currentRoute.value.fullPath).toBe('/catalogs/ab.accounting_policy')
  })

  it('redirects accounting policy id route back to the singleton page', async () => {
    const screen = await renderAt('/catalogs/ab.accounting_policy/policy-1')

    await expect.element(screen.getByTestId('accounting-policy-page')).toBeInTheDocument()
    expect(router.currentRoute.value.fullPath).toBe('/catalogs/ab.accounting_policy')
  })

  it('renders metadata-driven document list routes through the shared shell', async () => {
    const screen = await renderAt('/documents/ab.sales_invoice')

    await expect.element(screen.getByTestId('document-list-page')).toBeInTheDocument()
  })

  it('renders report routes through the report page component', async () => {
    const screen = await renderAt('/reports/ab.ar_aging')

    await expect.element(screen.getByTestId('report-page')).toBeInTheDocument()
  })

  it('passes accounting back target into the period closing page', async () => {
    const screen = await renderAt('/admin/accounting/period-closing')

    await expect.element(screen.getByTestId('period-closing-page')).toHaveTextContent('/admin/chart-of-accounts')
    expect(mocks.buildChartOfAccountsPath).toHaveBeenCalled()
  })
})
