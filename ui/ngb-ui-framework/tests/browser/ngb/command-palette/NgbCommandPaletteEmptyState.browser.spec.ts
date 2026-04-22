import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbCommandPaletteEmptyState from '../../../../src/ngb/command-palette/NgbCommandPaletteEmptyState.vue'

const EmptyHarness = defineComponent({
  props: {
    query: { type: String, required: true },
    loading: { type: Boolean, required: true },
    error: { type: String, default: null },
  },
  setup(props) {
    return () => h(NgbCommandPaletteEmptyState, props)
  },
})

test('renders onboarding and no-results states with scope hints', async () => {
  await page.viewport(1024, 800)

  const emptyView = await render(EmptyHarness, {
    props: {
      query: '',
      loading: false,
      error: null,
    },
  })

  await expect.element(emptyView.getByText('Search pages, records, reports, or run a command')).toBeVisible()
  await expect.element(emptyView.getByText('trial balance')).toBeVisible()
  await expect.element(emptyView.getByText('Commands')).toBeVisible()
  await expect.element(emptyView.getByText('Catalogs')).toBeVisible()

  const noResultsView = await render(EmptyHarness, {
    props: {
      query: 'lease',
      loading: false,
      error: null,
    },
  })

  await expect.element(noResultsView.getByText('No results for “lease”')).toBeVisible()
  await expect.element(noResultsView.getByText('Try a document number, a page name, or a scope prefix to narrow the search.')).toBeVisible()
})

test('renders loading and remote-search error states', async () => {
  await page.viewport(1024, 800)

  const loadingView = await render(EmptyHarness, {
    props: {
      query: 'chart',
      loading: true,
      error: null,
    },
  })

  await expect.element(loadingView.getByText('Searching records…')).toBeVisible()
  await expect.element(loadingView.getByText('Pages, commands, and cached reports stay available while remote results load.')).toBeVisible()
  const status = loadingView.getByText('Searching records…').element().closest('[role="status"]') as HTMLElement | null
  expect(status).not.toBeNull()
  expect(status?.getAttribute('aria-live')).toBe('polite')

  const errorView = await render(EmptyHarness, {
    props: {
      query: 'chart',
      loading: false,
      error: 'Search API offline',
    },
  })

  await expect.element(errorView.getByText('Remote search is temporarily unavailable')).toBeVisible()
  await expect.element(errorView.getByText('Search API offline')).toBeVisible()
  const alert = errorView.getByText('Remote search is temporarily unavailable').element().closest('[role="alert"]') as HTMLElement | null
  expect(alert).not.toBeNull()
  expect(alert?.getAttribute('aria-live')).toBe('assertive')
})
