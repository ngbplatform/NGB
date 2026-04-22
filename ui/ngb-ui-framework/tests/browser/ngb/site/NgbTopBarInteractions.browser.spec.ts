import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbTopBar from '../../../../src/ngb/site/NgbTopBar.vue'

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

function visibleButtonByTitle(title: string): HTMLElement {
  const button = Array.from(document.querySelectorAll(`button[title="${title}"]`)).find(isVisible) as HTMLElement | undefined
  if (!button) throw new Error(`Visible button not found for title: ${title}`)
  return button
}

function visibleUserButton(): HTMLElement {
  return visibleButtonByTitle('User')
}

function visibleButtonContainingText(text: string): HTMLElement {
  const button = Array.from(document.querySelectorAll('button')).find((element) => (
    isVisible(element) && element.textContent?.includes(text)
  )) as HTMLElement | undefined

  if (!button) throw new Error(`Visible button not found containing text: ${text}`)
  return button
}

function hasVisibleText(text: string): boolean {
  return Array.from(document.querySelectorAll('body *')).some((element) => (
    isVisible(element) && element.textContent?.trim() === text
  ))
}

const TopBarEventHarness = defineComponent({
  setup() {
    const mainMenu = ref(0)
    const palette = ref(0)
    const notifications = ref(0)
    const help = ref(0)
    const settings = ref(0)
    const signOut = ref(0)
    const theme = ref(0)

    return () => h('div', [
      h(NgbTopBar, {
        pageTitle: 'Payments',
        canBack: false,
        unreadNotifications: 128,
        themeResolved: 'dark',
        userName: 'Alex Carter',
        userEmail: 'alex.carter@example.com',
        userMeta: 'Admin access',
        userMetaIcon: 'shield-check',
        hasSettings: true,
        showMainMenu: true,
        onOpenMainMenu: () => { mainMenu.value += 1 },
        onOpenPalette: () => { palette.value += 1 },
        onOpenNotifications: () => { notifications.value += 1 },
        onOpenHelp: () => { help.value += 1 },
        onOpenSettings: () => { settings.value += 1 },
        onSignOut: () => { signOut.value += 1 },
        onToggleTheme: () => { theme.value += 1 },
      }),
      h('div', { 'data-testid': 'topbar-main-menu-count' }, String(mainMenu.value)),
      h('div', { 'data-testid': 'topbar-palette-count' }, String(palette.value)),
      h('div', { 'data-testid': 'topbar-notifications-count' }, String(notifications.value)),
      h('div', { 'data-testid': 'topbar-help-count' }, String(help.value)),
      h('div', { 'data-testid': 'topbar-settings-count' }, String(settings.value)),
      h('div', { 'data-testid': 'topbar-signout-count' }, String(signOut.value)),
      h('div', { 'data-testid': 'topbar-theme-count' }, String(theme.value)),
    ])
  },
})

const MobileTopBarHarness = defineComponent({
  setup() {
    const mainMenu = ref(0)

    return () => h('div', [
      h(NgbTopBar, {
        pageTitle: 'Home',
        canBack: false,
        unreadNotifications: 128,
        themeResolved: 'light',
        userName: 'Jordan',
        hasSettings: false,
        showMainMenu: true,
        onOpenMainMenu: () => { mainMenu.value += 1 },
      }),
      h('div', { 'data-testid': 'mobile-main-menu-count' }, String(mainMenu.value)),
    ])
  },
})

test('emits desktop actions and exposes the full user menu state', async () => {
  await page.viewport(1440, 900)

  const view = await render(TopBarEventHarness)

  expect(hasVisibleText('99+')).toBe(true)
  await expect.element(view.getByRole('button', { name: /Search pages, records, reports, or run a command/i })).toBeVisible()
  expect(visibleUserButton().textContent?.trim()).toBe('AC')
  expect(hasVisibleText('⌘')).toBe(true)

  await view.getByRole('button', { name: /Search pages, records, reports, or run a command/i }).click()
  await visibleButtonByTitle('Notifications').click()
  await visibleButtonByTitle('Help').click()
  await visibleButtonByTitle('Settings').click()
  await visibleButtonByTitle('Switch to light mode').click()

  await visibleUserButton().click()

  await expect.element(view.getByText('Signed in', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Alex Carter', { exact: true })).toBeVisible()
  await expect.element(view.getByText('alex.carter@example.com', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Admin access', { exact: true })).toBeVisible()

  await visibleButtonContainingText('Sign out').click()

  await expect.element(view.getByTestId('topbar-palette-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('topbar-notifications-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('topbar-help-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('topbar-settings-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('topbar-theme-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('topbar-signout-count')).toHaveTextContent('1')
})

test('emits mobile main-menu actions and hides the settings shortcut when none exist', async () => {
  await page.viewport(375, 800)

  const view = await render(MobileTopBarHarness)

  await expect.element(view.getByTestId('site-topbar-main-menu')).toBeVisible()
  expect(hasVisibleText('99+')).toBe(true)

  expect(Array.from(document.querySelectorAll('button[title="Settings"]')).some(isVisible)).toBe(false)

  await view.getByTestId('site-topbar-main-menu').click()

  await expect.element(view.getByTestId('mobile-main-menu-count')).toHaveTextContent('1')
})
