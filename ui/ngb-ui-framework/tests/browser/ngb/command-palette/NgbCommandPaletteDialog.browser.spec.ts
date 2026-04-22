import { page } from 'vitest/browser'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref, watch } from 'vue'
import { RouterView, createMemoryHistory, createRouter, useRoute } from 'vue-router'

import NgbCommandPaletteDialog from '../../../../src/ngb/command-palette/NgbCommandPaletteDialog.vue'
import { configureNgbCommandPalette } from '../../../../src/ngb/command-palette/config'
import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'
import { useCommandPalettePageContext } from '../../../../src/ngb/command-palette/useCommandPalettePageContext'
import { useCommandPaletteHotkeys } from '../../../../src/ngb/command-palette/useCommandPaletteHotkeys'
import { useMainMenuStore, type MainMenuGroup } from '../../../../src/ngb/site/mainMenuStore'
import type { CommandPaletteSearchResponseDto, CommandPaletteStoreConfig } from '../../../../src/ngb/command-palette/types'

const recentStorageKey = 'ngb:test:command-palette:browser'
const invoiceId = '123e4567-e89b-12d3-a456-426614174000'

const paletteBrowserMocks = vi.hoisted(() => ({
  approveInvoice: vi.fn(),
}))

const menuGroups: MainMenuGroup[] = [
  {
    label: 'Home',
    ordinal: 0,
    icon: 'home',
    items: [
      { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
    ],
  },
  {
    label: 'Payables',
    ordinal: 10,
    icon: 'wallet',
    items: [
      { kind: 'page', code: 'payables-open-items', label: 'Payables', route: '/payables/open-items', icon: 'wallet', ordinal: 0 },
    ],
  },
]

const specialPageItems: NonNullable<CommandPaletteStoreConfig['specialPageItems']> = [
  {
    key: 'page:special-settings',
    group: 'go-to',
    kind: 'page',
    scope: 'pages',
    title: 'Settings',
    subtitle: 'Administration',
    icon: 'settings',
    badge: 'Page',
    hint: null,
    route: '/settings',
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: ['settings', 'admin'],
    defaultRank: 560,
  },
]

const InvoiceRouteWithContext = defineComponent({
  setup() {
    useCommandPalettePageContext(() => ({
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: invoiceId,
      title: 'Invoice INV-001',
      actions: [
        {
          key: 'current:approve-invoice',
          group: 'actions',
          kind: 'command',
          scope: 'commands',
          title: 'Approve invoice',
          subtitle: 'Approve this draft',
          icon: 'check',
          badge: 'Approve',
          hint: null,
          route: null,
          commandCode: 'approve-invoice',
          status: null,
          openInNewTabSupported: false,
          keywords: ['approve', 'invoice'],
          defaultRank: 990,
          perform: paletteBrowserMocks.approveInvoice,
          isCurrentContext: true,
        },
      ],
    }))

    return () => h('div', 'Invoice route')
  },
})

const PaletteHarness = defineComponent({
  setup() {
    const route = useRoute()
    const palette = useCommandPaletteStore()
    const menuStore = useMainMenuStore()

    useCommandPaletteHotkeys()
    menuStore.$patch({ groups: menuGroups })

    watch(
      () => route.fullPath,
      (fullPath) => {
        palette.setCurrentRoute(fullPath)
      },
      { immediate: true },
    )

    return () => h('div', { style: 'padding: 24px;' }, [
      h('button', {
        type: 'button',
        'data-testid': 'palette-opener',
        onClick: () => palette.open(),
      }, 'Open palette'),
      h('input', {
        'data-testid': 'inline-editor',
        placeholder: 'Inline editor',
        style: 'display: block; margin-top: 16px;',
      }),
      h('div', {
        'data-testid': 'current-route',
        style: 'margin-top: 16px;',
      }, route.fullPath),
      h('div', {
        'data-testid': 'route-context-host',
        style: 'display: none;',
      }, [h(RouterView)]),
      h(NgbCommandPaletteDialog),
    ])
  },
})

const HotkeyListenerChild = defineComponent({
  setup() {
    useCommandPaletteHotkeys()

    return () => h('div', { 'data-testid': 'hotkey-listener-child' }, 'Hotkeys mounted')
  },
})

const HotkeyRemountHarness = defineComponent({
  setup() {
    const palette = useCommandPaletteStore()
    const mounted = ref(true)

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          mounted.value = !mounted.value
        },
      }, mounted.value ? 'Unmount hotkeys' : 'Mount hotkeys'),
      h('button', {
        type: 'button',
        onClick: () => {
          palette.close()
        },
      }, 'Close palette'),
      mounted.value ? h(HotkeyListenerChild) : null,
      h('div', { 'data-testid': 'hotkey-focus-key' }, String(palette.focusRequestKey)),
      h('div', { 'data-testid': 'hotkey-open-state' }, String(palette.isOpen)),
    ])
  },
})

function dispatchShortcut(target: Window | HTMLElement): void {
  const isMac = /Mac|iPhone|iPad|iPod/i.test(String(navigator.platform ?? ''))
  const event = new KeyboardEvent('keydown', {
    key: 'k',
    metaKey: isMac,
    ctrlKey: !isMac,
    bubbles: true,
    cancelable: true,
  })

  target.dispatchEvent(event)
}

function dispatchKey(target: HTMLElement, key: string): void {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }))
}

async function wait(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

function isMacPlatform() {
  return /Mac|iPhone|iPad|iPod/i.test(String(navigator.platform ?? ''))
}

function paletteBackdrop(): HTMLDivElement {
  const backdrop = Array.from(document.querySelectorAll('div.fixed.inset-0')).find((node) => node.childElementCount === 0)
  expect(backdrop).toBeTruthy()
  return backdrop as HTMLDivElement
}

function dispatchBackdropInteraction(target: HTMLElement) {
  target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
}

function optionByItemKey(itemKey: string): HTMLElement {
  const option = document.querySelector<HTMLElement>(`[data-item-key="${itemKey}"]`)
  expect(option).toBeTruthy()
  return option as HTMLElement
}

async function renderPaletteHarness(
  initialRoute = '/home',
  options: {
    searchRemote?: CommandPaletteStoreConfig['searchRemote']
  } = {},
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/home', component: { template: '<div>Home route</div>' } },
      { path: '/payables/open-items', component: { template: '<div>Payables route</div>' } },
      { path: '/settings', component: { template: '<div>Settings route</div>' } },
      { path: '/documents/pm.invoice/:id', component: InvoiceRouteWithContext },
    ],
  })

  configureNgbCommandPalette({
    router,
    recentStorageKey,
    loadReportItems: async () => [],
    specialPageItems,
    searchRemote: options.searchRemote,
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(PaletteHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    router,
    ...view,
  }
}

async function renderHotkeyRemountHarness() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/home', component: { template: '<div>Home route</div>' } },
    ],
  })

  configureNgbCommandPalette({
    router,
    recentStorageKey,
    loadReportItems: async () => [],
    specialPageItems,
  })

  await router.push('/home')
  await router.isReady()

  return await render(HotkeyRemountHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })
}

beforeEach(() => {
  window.localStorage.removeItem(recentStorageKey)
  vi.clearAllMocks()
})

afterEach(() => {
  document.documentElement.classList.remove('dark')
})

test('opens from the keyboard shortcut and focuses the combobox', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  dispatchShortcut(window)

  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  expect(document.activeElement).toBe(view.getByTestId('command-palette-input').element())
})

test('ignores the shortcut while another editable input is focused', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()
  const editor = view.getByTestId('inline-editor')

  await editor.click()
  dispatchShortcut(editor.element() as HTMLElement)

  expect(document.activeElement).toBe(editor.element())
  expect(document.querySelector('[data-testid=\"command-palette-dialog\"]')).toBeNull()
})

test('filters local menu items and navigates on Enter', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('payables')

  const payablesOption = view.getByRole('option', { name: /Payables/i })
  await expect.element(payablesOption).toBeVisible()
  ;(payablesOption.element() as HTMLElement).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))
  dispatchKey(input.element() as HTMLElement, 'Enter')

  await wait(180)
  expect(view.getByTestId('current-route').element().textContent).toBe('/payables/open-items')
  expect(document.querySelector('[data-testid=\"command-palette-dialog\"]')).toBeNull()
})

test('navigates local results using only the keyboard after opening from the global shortcut', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  dispatchShortcut(window)
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('payables')
  await expect.element(view.getByRole('option', { name: /Payables/i })).toBeVisible()

  dispatchKey(input.element() as HTMLElement, 'ArrowDown')
  await wait()
  expect(String((input.element() as HTMLInputElement).getAttribute('aria-activedescendant') ?? '')).toContain('ngb-command-palette-option-')

  dispatchKey(input.element() as HTMLElement, 'Enter')
  await wait(180)

  expect(view.getByTestId('current-route').element().textContent).toBe('/payables/open-items')
  expect(document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('closes on Escape after opening from the keyboard shortcut', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()
  const opener = view.getByTestId('palette-opener')

  ;(opener.element() as HTMLElement).focus()
  dispatchShortcut(window)
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  dispatchKey(view.getByTestId('command-palette-input').element() as HTMLElement, 'Escape')

  await wait(180)
  expect(document.querySelector('[data-testid=\"command-palette-dialog\"]')).toBeNull()
})

test('clears the previous query when reopened after Escape', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()
  const opener = view.getByTestId('palette-opener')

  ;(opener.element() as HTMLElement).focus()
  dispatchShortcut(window)
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('payables')
  dispatchKey(input.element() as HTMLElement, 'Escape')

  await wait(180)
  expect(document.querySelector('[data-testid=\"command-palette-dialog\"]')).toBeNull()

  dispatchShortcut(window)
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  expect((view.getByTestId('command-palette-input').element() as HTMLInputElement).value).toBe('')
})

test('closes cleanly after Escape when opened from an external launcher button', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  dispatchKey(view.getByTestId('command-palette-input').element() as HTMLElement, 'Escape')
  await wait(260)
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('closes when the backdrop is clicked after launcher-button open', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  dispatchBackdropInteraction(paletteBackdrop())
  await wait(260)
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('keeps only the newest remote results when slower stale searches resolve later', async () => {
  await page.viewport(1024, 900)

  let resolveLease!: (value: CommandPaletteSearchResponseDto) => void
  let resolveInvoice!: (value: CommandPaletteSearchResponseDto) => void

  const searchRemote: NonNullable<CommandPaletteStoreConfig['searchRemote']> = async ({ query }) => {
    if (query === 'lease') {
      return await new Promise<CommandPaletteSearchResponseDto>((resolve) => {
        resolveLease = resolve
      })
    }

    return await new Promise<CommandPaletteSearchResponseDto>((resolve) => {
      resolveInvoice = resolve
    })
  }

  const view = await renderPaletteHarness('/home', { searchRemote })
  await view.getByTestId('palette-opener').click()

  const input = view.getByTestId('command-palette-input')
  await input.fill('lease')
  await wait(180)
  await input.fill('invoice')
  await wait(180)

  resolveInvoice({
    groups: [
      {
        code: 'documents',
        label: 'Documents',
        items: [
          {
            key: 'invoice-1',
            kind: 'document',
            title: 'Invoice INV-001',
            subtitle: 'Receivables',
            route: '/documents/pm.invoice/invoice-1',
            openInNewTabSupported: true,
            score: 10,
          },
        ],
      },
    ],
  })
  await wait()
  await expect.element(view.getByRole('option', { name: /Invoice INV-001/i })).toBeVisible()

  resolveLease({
    groups: [
      {
        code: 'documents',
        label: 'Documents',
        items: [
          {
            key: 'lease-1',
            kind: 'document',
            title: 'Lease AGR-001',
            subtitle: 'Legacy result',
            route: '/documents/pm.lease/lease-1',
            openInNewTabSupported: true,
            score: 10,
          },
        ],
      },
    ],
  })
  await wait()

  expect(document.body.textContent).toContain('Invoice INV-001')
  expect(document.body.textContent).not.toContain('Lease AGR-001')
})

test('restores launcher focus and exposes combobox accessibility state while the palette is open', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()
  const opener = view.getByTestId('palette-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input').element() as HTMLInputElement
  expect(document.activeElement).toBe(input)
  expect(input.getAttribute('role')).toBe('combobox')
  expect(input.getAttribute('aria-controls')).toBe('ngb-command-palette-listbox')

  dispatchKey(input, 'ArrowDown')
  await wait()

  expect(String(input.getAttribute('aria-activedescendant') ?? '')).toContain('ngb-command-palette-option-')
  expect(document.querySelector('[role="listbox"]')).not.toBeNull()
  expect((document.querySelector('[aria-live="polite"]')?.textContent ?? '')).toContain('results available')

  dispatchKey(input, 'Escape')
  await wait(260)

  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(opener.element())
  })
})

test('opens the active item in a new tab when Ctrl/Cmd+Enter is pressed', async () => {
  await page.viewport(1024, 900)

  const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null)
  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('payables')

  const payablesOption = view.getByRole('option', { name: /Payables/i })
  await expect.element(payablesOption).toBeVisible()
  ;(payablesOption.element() as HTMLElement).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))

  input.element().dispatchEvent(new KeyboardEvent('keydown', {
    key: 'Enter',
    bubbles: true,
    cancelable: true,
    metaKey: isMacPlatform(),
    ctrlKey: !isMacPlatform(),
  }))

  await wait(220)

  expect(openSpy).toHaveBeenCalledWith('/payables/open-items', '_blank', 'noopener,noreferrer')
  expect(view.getByTestId('current-route').element().textContent).toBe('/home')
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('renders special pages from store config and persists them as recents across store instances', async () => {
  await page.viewport(1024, 900)

  const first = await renderPaletteHarness()

  await first.getByTestId('palette-opener').click()
  await expect.element(first.getByTestId('command-palette-dialog')).toBeVisible()

  const firstInput = first.getByTestId('command-palette-input')
  await firstInput.fill('settings')

  const settingsOption = first.getByRole('option', { name: /Settings/i })
  await expect.element(settingsOption).toBeVisible()
  ;(settingsOption.element() as HTMLElement).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))
  dispatchKey(firstInput.element() as HTMLElement, 'Enter')

  await wait(180)
  expect(first.getByTestId('current-route').element().textContent).toBe('/settings')
  first.unmount()

  const second = await renderPaletteHarness()

  await second.getByTestId('palette-opener').click()
  await expect.element(second.getByTestId('command-palette-dialog')).toBeVisible()
  await expect.element(second.getByText('Recent')).toBeVisible()
  await expect.poll(() => optionByItemKey('recent:page:special-settings')).toBeTruthy()

  optionByItemKey('recent:page:special-settings').dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await wait(180)

  expect(second.getByTestId('current-route').element().textContent).toBe('/settings')
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('ignores malformed recent-entry storage and still opens local results', async () => {
  await page.viewport(1024, 900)
  window.localStorage.setItem(recentStorageKey, '{broken-json')

  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  await expect.element(view.getByRole('option', { name: /Home/i })).toBeVisible()
  expect(document.querySelector('[data-item-key^="recent:"]')).toBeNull()
})

test('shows explicit page-context actions in the dialog and executes them from the real UI', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness(`/documents/pm.invoice/${invoiceId}`)

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('approve')

  const approveOption = view.getByRole('option', { name: /Approve invoice/i })
  await expect.element(approveOption).toBeVisible()
  ;(approveOption.element() as HTMLElement).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))
  dispatchKey(input.element() as HTMLElement, 'Enter')

  await wait(180)

  expect(paletteBrowserMocks.approveInvoice).toHaveBeenCalledTimes(1)
  expect(view.getByTestId('current-route').element().textContent).toBe(`/documents/pm.invoice/${invoiceId}`)
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
})

test('keeps the teleported palette mounted and focused while the theme class changes live', async () => {
  await page.viewport(1024, 900)

  const view = await renderPaletteHarness()

  await view.getByTestId('palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const input = view.getByTestId('command-palette-input').element() as HTMLInputElement
  expect(document.activeElement).toBe(input)

  document.documentElement.classList.add('dark')
  await wait()

  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  expect(document.activeElement).toBe(input)

  document.documentElement.classList.remove('dark')
  await wait()

  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  expect(document.activeElement).toBe(input)
})

test('removes the global shortcut listener on unmount so remounting does not duplicate opens', async () => {
  await page.viewport(1024, 900)

  const view = await renderHotkeyRemountHarness()

  dispatchShortcut(window)
  await expect.element(view.getByTestId('hotkey-open-state')).toHaveTextContent('true')
  await expect.element(view.getByTestId('hotkey-focus-key')).toHaveTextContent('1')

  await view.getByRole('button', { name: 'Close palette' }).click()
  await expect.element(view.getByTestId('hotkey-open-state')).toHaveTextContent('false')

  await view.getByRole('button', { name: 'Unmount hotkeys' }).click()
  await expect.element(view.getByRole('button', { name: 'Mount hotkeys' })).toBeVisible()
  await view.getByRole('button', { name: 'Mount hotkeys' }).click()

  dispatchShortcut(window)
  await expect.element(view.getByTestId('hotkey-open-state')).toHaveTextContent('true')
  await expect.element(view.getByTestId('hotkey-focus-key')).toHaveTextContent('2')
})
