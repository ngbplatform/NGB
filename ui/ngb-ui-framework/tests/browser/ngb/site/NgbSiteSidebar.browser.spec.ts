import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbSiteSidebar from '../../../../src/ngb/site/NgbSiteSidebar.vue'
import type { SiteNavNode } from '../../../../src/ngb/site/types'

function rect(locator: { element(): Element }): DOMRect {
  return locator.element().getBoundingClientRect()
}

function text(locator: { element(): Element }): string {
  return locator.element().textContent?.trim() ?? ''
}

const sidebarNodes: SiteNavNode[] = [
  { id: 'dashboard', label: 'Dashboard', route: '/dashboard', icon: 'home' },
  {
    id: 'receivables',
    label: 'Receivables',
    icon: 'coins',
    children: [
      { id: 'payments', label: 'Payments', route: '/receivables/payments', icon: 'credit-card' },
      { id: 'returns', label: 'Returns', route: '/receivables/returns', icon: 'rotate-ccw' },
    ],
  },
  {
    id: 'reports',
    label: 'Reports',
    icon: 'bar-chart-3',
    children: [
      { id: 'aging', label: 'Aging', route: '/reports/aging', icon: 'line-chart' },
      { id: 'archive', label: 'Archive', route: '/reports/archive', icon: 'archive', disabled: true },
    ],
  },
]

const SidebarHarness = defineComponent({
  setup() {
    const collapsed = ref(false)
    const selectedId = ref<string | null>('payments')
    const lastRoute = ref('none')
    const lastSelected = ref('none')

    function handleSelect(id: string, route: string) {
      selectedId.value = id
      lastSelected.value = `${id}:${route}`
    }

    function handleNavigate(route: string) {
      lastRoute.value = route
    }

    return () => h('div', { class: 'flex gap-4' }, [
      h('div', { class: 'h-[900px] w-[320px]' }, [
        h(NgbSiteSidebar, {
          productTitle: 'NGB',
          brandSubtitle: 'Property Management',
          pinned: [],
          recent: [],
          nodes: sidebarNodes,
          selectedId: selectedId.value,
          collapsed: collapsed.value,
          onToggleCollapsed: () => {
            collapsed.value = !collapsed.value
          },
          onSelect: handleSelect,
          onNavigate: handleNavigate,
        }),
      ]),
      h('div', { class: 'space-y-2 text-sm' }, [
        h('div', { 'data-testid': 'sidebar-last-selected' }, lastSelected.value),
        h('div', { 'data-testid': 'sidebar-last-route' }, lastRoute.value),
        h('div', { 'data-testid': 'sidebar-collapsed-state' }, collapsed.value ? 'collapsed' : 'expanded'),
      ]),
    ])
  },
})

test('collapses into icon mode and opens a flyout for section children', async () => {
  await page.viewport(1280, 900)

  const view = await render(SidebarHarness)

  const sidebar = view.getByTestId('site-sidebar')

  await expect.element(sidebar).toBeVisible()
  await expect.element(view.getByTitle('Collapse sidebar')).toBeVisible()
  expect(Math.round(rect(sidebar).width)).toBe(320)

  await view.getByTitle('Collapse sidebar').click()

  await expect.element(view.getByTitle('Expand sidebar')).toBeVisible()
  expect(text(view.getByTestId('sidebar-collapsed-state'))).toBe('collapsed')
  expect(Math.round(rect(sidebar).width)).toBe(72)

  await view.getByTitle('Receivables').click()

  await expect.element(view.getByTestId('site-sidebar-flyout')).toBeVisible()
  await expect.element(view.getByText('Payments', { exact: true })).toBeVisible()

  await view.getByText('Payments', { exact: true }).click()

  expect(text(view.getByTestId('sidebar-last-selected'))).toBe('payments:/receivables/payments')
  expect(text(view.getByTestId('sidebar-last-route'))).toBe('/receivables/payments')
  expect(document.querySelector('[data-testid="site-sidebar-flyout"]')).toBeNull()
})

test('toggles expanded sections and emits navigation for visible leaf entries', async () => {
  await page.viewport(1280, 900)

  const view = await render(SidebarHarness)

  await expect.element(view.getByText('Receivables', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Payments', { exact: true })).toBeVisible()

  await view.getByText('Receivables', { exact: true }).click()
  expect(document.body.textContent?.includes('Payments')).toBe(false)

  await view.getByText('Receivables', { exact: true }).click()
  await expect.element(view.getByText('Payments', { exact: true })).toBeVisible()

  await view.getByText('Payments', { exact: true }).click()

  expect(text(view.getByTestId('sidebar-last-selected'))).toBe('payments:/receivables/payments')
  expect(text(view.getByTestId('sidebar-last-route'))).toBe('/receivables/payments')
})

test('switches collapsed flyout content between section roots and closes the same flyout on a second click', async () => {
  await page.viewport(1280, 900)

  const view = await render(SidebarHarness)

  await view.getByTitle('Collapse sidebar').click()
  await view.getByTitle('Receivables').click()

  const flyout = view.getByTestId('site-sidebar-flyout')

  await expect.element(flyout).toBeVisible()
  expect(text(flyout)).toContain('Payments')

  await view.getByTitle('Reports').click()

  await expect.element(flyout).toBeVisible()
  expect(text(flyout)).toContain('Aging')
  expect(text(flyout)).not.toContain('Payments')

  await view.getByTitle('Reports').click()

  expect(document.querySelector('[data-testid="site-sidebar-flyout"]')).toBeNull()
})

test('does not emit selection or navigation for disabled leaf entries', async () => {
  await page.viewport(1280, 900)

  const view = await render(SidebarHarness)

  await expect.element(view.getByText('Archive', { exact: true })).toBeVisible()
  await view.getByText('Archive', { exact: true }).click()

  expect(text(view.getByTestId('sidebar-last-selected'))).toBe('none')
  expect(text(view.getByTestId('sidebar-last-route'))).toBe('none')
})
