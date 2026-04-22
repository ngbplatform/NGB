import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'

import { encodeBackTarget } from '../../../../src/ngb/router/backNavigation'
import { encodeReportRouteContextParam, encodeReportSourceTrailParam } from '../../../../src/ngb/reporting/navigation'
import { ngbRouteAliasRedirectRoutes } from '../../../../src/ngb/router/routeAliases'

const DOCUMENT_ID = 'alias-document-001'

const RouteStatePage = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'alias-full-path' }, route.fullPath),
      h('div', { 'data-testid': 'alias-route-name' }, String(route.name ?? 'none')),
      h('div', { 'data-testid': 'alias-route-id' }, String(route.params.id ?? 'none')),
      h('div', { 'data-testid': 'alias-report-code' }, String(route.params.reportCode ?? 'none')),
    ])
  },
})

async function renderAliasHarness(initialRoute: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      ...ngbRouteAliasRedirectRoutes,
      { path: '/accounting/general-journal-entries', name: 'gje-list', component: RouteStatePage },
      { path: '/accounting/general-journal-entries/new', name: 'gje-new', component: RouteStatePage },
      { path: '/accounting/general-journal-entries/:id', name: 'gje-edit', component: RouteStatePage },
      { path: '/reports/:reportCode', name: 'report-page', component: RouteStatePage },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(defineComponent({
    setup() {
      return () => h(RouterView)
    },
  }), {
    global: {
      plugins: [router],
    },
  })

  return { router, view }
}

test('redirects legacy journal-entry aliases to the canonical route while preserving query, back target, and hash state', async () => {
  await page.viewport(1280, 900)

  const back = encodeBackTarget('/dashboard?tab=review')
  const initialRoute = `/documents/accounting.general_journal_entry/${DOCUMENT_ID}?back=${back}&ctx=legacy-ctx&src=legacy-src#drawer`

  const { view } = await renderAliasHarness(initialRoute)

  await expect.element(view.getByTestId('alias-route-name')).toHaveTextContent('gje-edit')
  await expect.element(view.getByTestId('alias-route-id')).toHaveTextContent(DOCUMENT_ID)
  await expect.poll(() => view.getByTestId('alias-full-path').element().textContent ?? '').toBe(
    `/accounting/general-journal-entries/${DOCUMENT_ID}?back=${back}&ctx=legacy-ctx&src=legacy-src#drawer`,
  )
})

test('redirects legacy report aliases to canonical report pages without dropping route context or source-trail state', async () => {
  await page.viewport(1280, 900)

  const back = encodeBackTarget('/home')
  const ctx = encodeReportRouteContextParam({
    reportCode: 'pm.portfolio.home',
    reportName: 'Portfolio Home',
    request: {
      parameters: null,
      filters: null,
      layout: null,
      offset: 0,
      limit: 500,
      cursor: null,
    },
  })
  const src = encodeReportSourceTrailParam({
    items: [
      {
        reportCode: 'pm.portfolio.home',
        reportName: 'Portfolio Home',
        request: {
          parameters: null,
          filters: null,
          layout: null,
          offset: 0,
          limit: 500,
          cursor: null,
        },
      },
    ],
  })

  const { view } = await renderAliasHarness(`/admin/accounting/posting-log?ctx=${ctx}&src=${src}&back=${back}`)

  await expect.element(view.getByTestId('alias-route-name')).toHaveTextContent('report-page')
  await expect.element(view.getByTestId('alias-report-code')).toHaveTextContent('accounting.posting_log')
  await expect.poll(() => view.getByTestId('alias-full-path').element().textContent ?? '').toBe(
    `/reports/accounting.posting_log?ctx=${ctx}&src=${src}&back=${back}`,
  )
})
