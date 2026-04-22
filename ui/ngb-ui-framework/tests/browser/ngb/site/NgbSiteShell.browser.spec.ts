import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

function rect(locator: { element(): Element }): DOMRect {
  return locator.element().getBoundingClientRect()
}

function scrollMetrics(locator: { element(): Element }) {
  const element = locator.element() as HTMLElement
  return {
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
    scrollTop: element.scrollTop,
  }
}

function hasDisplayNoneInAncestors(element: Element): boolean {
  let current: Element | null = element

  while (current) {
    if (window.getComputedStyle(current as HTMLElement).display === 'none') {
      return true
    }
    current = current.parentElement
  }

  return false
}

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
      { label: 'Preferences', route: '/settings/preferences', icon: 'settings' },
    ],
  },
]

const SiteShellHarness = defineComponent({
  setup() {
    return () => h(
      NgbSiteShell,
      {
        moduleTitle: 'Property Management',
        productTitle: 'NGB',
        userName: 'Alex Carter',
        userEmail: 'alex.carter@example.com',
        pinned: [],
        recent: [],
        nodes: shellNodes,
        settings: shellSettings,
        selectedId: 'payments',
        pageTitle: 'Payments',
      },
      {
        default: () => h(
          'div',
          {
            'data-testid': 'shell-scroll-region',
            class: 'flex-1 min-h-0 overflow-auto',
          },
          [
            h('div', { class: 'p-4 font-medium text-ngb-text' }, 'Scrollable workspace'),
            h('div', { style: 'height: 1800px' }),
          ],
        ),
      },
    )
  },
})

test('keeps the desktop shell full-height and moves scrolling into the workspace region', async () => {
  await page.viewport(1440, 900)

  const view = await render(SiteShellHarness)

  const shell = view.getByTestId('site-shell')
  const sidebar = view.getByTestId('site-sidebar')
  const brand = view.getByTestId('site-sidebar-brand')
  const topbar = view.getByTestId('site-topbar')
  const scrollRegion = view.getByTestId('shell-scroll-region')

  await expect.element(shell).toBeVisible()
  await expect.element(sidebar).toBeVisible()
  await expect.element(brand).toBeVisible()
  await expect.element(topbar).toBeVisible()
  await expect.element(scrollRegion).toBeVisible()

  expect(Math.round(rect(shell).height)).toBe(900)
  expect(Math.round(rect(sidebar).height)).toBeGreaterThanOrEqual(899)
  expect(Math.round(rect(brand).height)).toBe(56)

  const initialScrollMetrics = scrollMetrics(scrollRegion)
  expect(initialScrollMetrics.scrollHeight).toBeGreaterThan(initialScrollMetrics.clientHeight)

  const regionElement = scrollRegion.element() as HTMLElement
  regionElement.scrollTop = 420

  const nextScrollMetrics = scrollMetrics(scrollRegion)
  expect(nextScrollMetrics.scrollTop).toBeGreaterThan(0)
  expect(window.scrollY).toBe(0)
  expect(document.documentElement.scrollHeight <= window.innerHeight + 1).toBe(true)
})

test('hides the desktop sidebar below the md breakpoint', async () => {
  await page.viewport(375, 800)

  const view = await render(SiteShellHarness)

  const shell = view.getByTestId('site-shell')
  const sidebar = view.getByTestId('site-sidebar')
  const topbar = view.getByTestId('site-topbar')
  const mainMenuButton = view.getByTestId('site-topbar-main-menu')

  await expect.element(shell).toBeVisible()
  await expect.element(topbar).toBeVisible()
  await expect.element(mainMenuButton).toBeVisible()

  expect(hasDisplayNoneInAncestors(sidebar.element())).toBe(true)
  expect(Math.round(rect(shell).height)).toBe(800)
  expect(document.documentElement.scrollHeight <= window.innerHeight + 1).toBe(true)

  await mainMenuButton.click()

  const drawerPanel = document.querySelector('[data-testid="drawer-panel"]') as HTMLElement | null
  const mobileSidebar = document.querySelector('[data-testid="mobile-main-menu-sidebar"]') as HTMLElement | null

  expect(drawerPanel).not.toBeNull()
  expect(mobileSidebar).not.toBeNull()
  expect(mobileSidebar!.textContent?.includes('Receivables')).toBe(true)
})
