import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbTopBar from '../../../../src/ngb/site/NgbTopBar.vue'

function rect(locator: { element(): Element }): DOMRect {
  return locator.element().getBoundingClientRect()
}

function isVisible(element: Element): boolean {
  let current: Element | null = element

  while (current) {
    const style = window.getComputedStyle(current as HTMLElement)
    if (style.display === 'none' || style.visibility === 'hidden') {
      return false
    }
    current = current.parentElement
  }

  return true
}

function visibleUserButton(): HTMLElement {
  const button = Array.from(document.querySelectorAll('[title="User"]')).find(isVisible) as HTMLElement | undefined
  if (!button) throw new Error('Visible user button not found')
  return button
}

const TopBarHarness = defineComponent({
  setup() {
    return () => h(NgbTopBar, {
      pageTitle: 'Payments',
      canBack: false,
      unreadNotifications: 7,
      themeResolved: 'light',
      userName: 'Alex Carter',
      userEmail: 'alex.carter@example.com',
      hasSettings: true,
      showMainMenu: true,
    })
  },
})

const TabletTopBarHarness = defineComponent({
  setup() {
    return () => h(
      'div',
      {
        style: 'width: 448px; max-width: 448px;',
      },
      [
        h(NgbTopBar, {
          pageTitle: 'Ledger Analysis',
          canBack: false,
          unreadNotifications: 3,
          themeResolved: 'light',
          userName: 'Alex Carter',
          userEmail: 'alex.carter@example.com',
          hasSettings: true,
          showMainMenu: true,
        }),
      ],
    )
  },
})

test('stacks the search bar below utility actions on narrow viewports', async () => {
  await page.viewport(375, 800)

  const view = await render(TopBarHarness)

  const topbar = view.getByTestId('site-topbar')
  const mainMenuButton = view.getByTestId('site-topbar-main-menu')
  const search = view.getByRole('button', { name: /Search pages, records, reports, or run a command/i })

  await expect.element(topbar).toBeVisible()
  await expect.element(mainMenuButton).toBeVisible()
  await expect.element(search).toBeVisible()

  const user = visibleUserButton()

  expect(rect(search).top).toBeGreaterThanOrEqual(user.getBoundingClientRect().bottom)
  expect(Math.round(rect(topbar).height)).toBeGreaterThan(56)
  expect(rect(mainMenuButton).left).toBeLessThan(user.getBoundingClientRect().left)
})

test('keeps the command palette full-width on tablet-sized shell widths', async () => {
  await page.viewport(768, 1024)

  const view = await render(TabletTopBarHarness)

  const topbar = view.getByTestId('site-topbar')
  const search = view.getByRole('button', { name: /Search pages, records, reports, or run a command/i })

  await expect.element(topbar).toBeVisible()
  await expect.element(search).toBeVisible()

  const user = visibleUserButton()

  expect(rect(search).top).toBeGreaterThanOrEqual(user.getBoundingClientRect().bottom)
  expect(Math.round(rect(search).width)).toBeGreaterThan(360)
  expect(Math.round(rect(topbar).height)).toBeGreaterThan(56)
})

test('keeps the top bar on a single 56px row on desktop viewports', async () => {
  await page.viewport(1440, 900)

  const view = await render(TopBarHarness)

  const topbar = view.getByTestId('site-topbar')
  const search = view.getByRole('button', { name: /Search pages, records, reports, or run a command/i })

  await expect.element(topbar).toBeVisible()
  await expect.element(search).toBeVisible()

  const user = visibleUserButton()

  expect(Math.round(rect(topbar).height)).toBe(56)
  expect(Math.abs(rect(search).top - user.getBoundingClientRect().top)).toBeLessThan(20)
})
