import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

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
  const button = Array.from(document.querySelectorAll('button')).find((entry) => (
    isVisible(entry) && entry.textContent?.includes(text)
  )) as HTMLElement | undefined

  if (!button) throw new Error(`Visible button not found containing text: ${text}`)
  return button
}

function visibleButtonByText(label: string): HTMLElement {
  const button = Array.from(document.querySelectorAll('button')).find((entry) => (
    isVisible(entry) && entry.textContent?.trim() === label
  )) as HTMLElement | undefined

  if (!button) throw new Error(`Visible button not found for label: ${label}`)
  return button
}

function createMatchMedia(matches: boolean) {
  return vi.fn().mockImplementation((query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }))
}

const shellNodes = [
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
]

const shellSettings = [
  {
    label: 'Workspace',
    items: [
      { label: 'Preferences', route: '/settings/preferences', icon: 'settings', description: 'Workspace defaults' },
      { label: 'Rules', route: '/settings/rules', icon: 'shield-check', description: 'Validation and policy' },
    ],
  },
]

const SiteShellInteractionHarness = defineComponent({
  setup() {
    const lastNavigate = ref('none')
    const lastSelect = ref('none')
    const paletteCount = ref(0)
    const signOutCount = ref(0)

    return () => h('div', [
      h(
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
          onNavigate: (route: string) => { lastNavigate.value = route },
          onSelect: (id: string, route: string) => { lastSelect.value = `${id}:${route}` },
          onOpenPalette: () => { paletteCount.value += 1 },
          onSignOut: () => { signOutCount.value += 1 },
        },
        {
          default: () => h('div', { 'data-testid': 'site-shell-content', class: 'flex-1 min-h-0 overflow-auto p-4' }, 'Workspace body'),
        },
      ),
      h('div', { 'data-testid': 'site-shell-last-navigate' }, lastNavigate.value),
      h('div', { 'data-testid': 'site-shell-last-select' }, lastSelect.value),
      h('div', { 'data-testid': 'site-shell-palette-count' }, String(paletteCount.value)),
      h('div', { 'data-testid': 'site-shell-signout-count' }, String(signOutCount.value)),
    ])
  },
})

test('switches shell drawers, navigates from settings, toggles theme, and forwards palette/sign-out actions', async () => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: createMatchMedia(false),
  })
  window.localStorage.removeItem('ngb.theme')
  document.documentElement.classList.remove('dark')

  await page.viewport(1440, 900)

  const view = await render(SiteShellInteractionHarness)

  await view.getByRole('button', { name: /Search pages, records, reports, or run a command/i }).click()
  await expect.element(view.getByTestId('site-shell-palette-count')).toHaveTextContent('1')

  await visibleButtonByTitle('Notifications').click()
  await expect.element(view.getByText('No notifications', { exact: true })).toBeVisible()

  await visibleButtonByTitle('Help').click()
  await expect.element(view.getByText('Help is coming soon', { exact: true })).toBeVisible()

  await visibleButtonByTitle('Settings').click()
  await expect.element(view.getByText('Preferences', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Rules', { exact: true })).toBeVisible()

  await view.getByText('Preferences', { exact: true }).click()

  await expect.element(view.getByTestId('site-shell-last-navigate')).toHaveTextContent('/settings/preferences')
  await vi.waitFor(() => {
    expect(document.querySelectorAll('[data-testid="drawer-panel"]').length).toBe(0)
  })

  expect(document.documentElement.classList.contains('dark')).toBe(false)
  await visibleButtonByTitle('Switch to dark mode').click()
  await vi.waitFor(() => {
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  await visibleUserButton().click()
  await visibleButtonContainingText('Sign out').click()
  await expect.element(view.getByTestId('site-shell-signout-count')).toHaveTextContent('1')
})

test('closes the mobile main-menu drawer and forwards select plus navigate events from sidebar leaves', async () => {
  await page.viewport(375, 800)

  const view = await render(SiteShellInteractionHarness)

  await expect.element(view.getByTestId('site-topbar-main-menu')).toBeVisible()

  await view.getByTestId('site-topbar-main-menu').click()
  await expect.element(view.getByTestId('mobile-main-menu-sidebar')).toBeVisible()

  await visibleButtonByText('Payments').click()

  await expect.element(view.getByTestId('site-shell-last-select')).toHaveTextContent('payments:/receivables/payments')
  await expect.element(view.getByTestId('site-shell-last-navigate')).toHaveTextContent('/receivables/payments')
  await vi.waitFor(() => {
    expect(document.querySelectorAll('[data-testid="drawer-panel"]').length).toBe(0)
  })
})
