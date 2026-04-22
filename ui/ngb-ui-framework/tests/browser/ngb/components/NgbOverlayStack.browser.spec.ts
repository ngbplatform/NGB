import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbCommandPaletteDialog from '../../../../src/ngb/command-palette/NgbCommandPaletteDialog.vue'
import { configureNgbCommandPalette } from '../../../../src/ngb/command-palette/config'
import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'
import { useCommandPaletteHotkeys } from '../../../../src/ngb/command-palette/useCommandPaletteHotkeys'
import NgbDrawer from '../../../../src/ngb/components/NgbDrawer.vue'
import NgbModalShell from '../../../../src/ngb/components/NgbModalShell.vue'
import NgbLookup from '../../../../src/ngb/primitives/NgbLookup.vue'

const recentStorageKey = 'ngb:test:overlay-stack'

const OverlayStackHarness = defineComponent({
  setup() {
    const drawerOpen = ref(false)
    const modalOpen = ref(false)
    const palette = useCommandPaletteStore()
    useCommandPaletteHotkeys()

    return () => h('div', { style: 'padding: 24px;' }, [
      h('button', {
        type: 'button',
        'data-testid': 'stack-root-opener',
        onClick: () => {
          drawerOpen.value = true
        },
      }, 'Open drawer'),
      h(
        NgbDrawer,
        {
          open: drawerOpen.value,
          title: 'Workspace drawer',
          subtitle: 'Layer one',
          'onUpdate:open': (value: boolean) => {
            drawerOpen.value = value
          },
        },
        {
          default: () => h('div', { class: 'space-y-4 p-4' }, [
            h('button', {
              type: 'button',
              'data-testid': 'stack-modal-opener',
              onClick: () => {
                modalOpen.value = true
              },
            }, 'Open modal'),
            h(
              NgbModalShell,
              {
                open: modalOpen.value,
                onClose: () => {
                  modalOpen.value = false
                },
              },
              {
                default: () => h('div', { class: 'space-y-3 p-4' }, [
                  h('div', { class: 'text-sm font-semibold text-ngb-text' }, 'Nested modal'),
                  h('button', {
                    type: 'button',
                    'data-testid': 'stack-palette-opener',
                    onClick: () => {
                      palette.open()
                    },
                  }, 'Open palette'),
                ]),
              },
            ),
            h(NgbCommandPaletteDialog),
          ]),
        },
      ),
      h('div', {
        'data-testid': 'stack-state',
      }, `drawer:${String(drawerOpen.value)};modal:${String(modalOpen.value)};palette:${String(palette.isOpen)}`),
    ])
  },
})

const OverlayLookupHarness = defineComponent({
  setup() {
    const drawerOpen = ref(false)
    const modalOpen = ref(false)
    const lookupValue = ref<{ id: string; label: string; meta?: string } | null>(null)
    const lookupItems = ref<Array<{ id: string; label: string; meta?: string }>>([])

    return () => h('div', { style: 'padding: 24px;' }, [
      h('button', {
        type: 'button',
        'data-testid': 'overlay-lookup-root-opener',
        onClick: () => {
          drawerOpen.value = true
        },
      }, 'Open drawer'),
      h(
        NgbDrawer,
        {
          open: drawerOpen.value,
          title: 'Workspace drawer',
          subtitle: 'Lookup stack',
          'onUpdate:open': (value: boolean) => {
            drawerOpen.value = value
          },
        },
        {
          default: () => h('div', { class: 'space-y-4 p-4' }, [
            h('button', {
              type: 'button',
              'data-testid': 'overlay-lookup-modal-opener',
              onClick: () => {
                modalOpen.value = true
              },
            }, 'Open lookup modal'),
            h(
              NgbModalShell,
              {
                open: modalOpen.value,
                onClose: () => {
                  modalOpen.value = false
                },
              },
              {
                default: () => h('div', { class: 'space-y-4 p-4' }, [
                  h('div', { class: 'text-sm font-semibold text-ngb-text' }, 'Lookup modal'),
                  h(
                    NgbLookup,
                    {
                      modelValue: lookupValue.value,
                      items: lookupItems.value,
                      label: 'Account',
                      placeholder: 'Search account',
                      showClear: true,
                      'onUpdate:modelValue': (value: { id: string; label: string; meta?: string } | null) => {
                        lookupValue.value = value
                      },
                      onQuery: (query: string) => {
                        lookupItems.value = query.trim()
                          ? [{ id: 'cash', label: '1100 Cash', meta: 'Operating account' }]
                          : []
                      },
                    },
                  ),
                ]),
              },
            ),
          ]),
        },
      ),
      h(
        'div',
        { 'data-testid': 'overlay-lookup-state' },
        `drawer:${String(drawerOpen.value)};modal:${String(modalOpen.value)};lookup:${lookupValue.value?.label ?? 'none'}`,
      ),
    ])
  },
})

function dispatchKey(target: HTMLElement, key: string): void {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }))
}

function dispatchShortcut(target: Window | HTMLElement): void {
  const isMac = /Mac|iPhone|iPad|iPod/i.test(String(navigator.platform ?? ''))
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key: 'k',
    metaKey: isMac,
    ctrlKey: !isMac,
    bubbles: true,
    cancelable: true,
  }))
}

async function renderOverlayStackHarness() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div>Home</div>' } },
    ],
  })

  configureNgbCommandPalette({
    router,
    recentStorageKey,
    loadReportItems: async () => [],
  })

  await router.push('/')
  await router.isReady()

  return await render(OverlayStackHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })
}

beforeEach(() => {
  window.localStorage.removeItem(recentStorageKey)
})

test('closes layered overlays from top to bottom and restores focus to the prior launcher each time', async () => {
  await page.viewport(1280, 900)

  const view = await renderOverlayStackHarness()
  const rootOpener = view.getByTestId('stack-root-opener')

  ;(rootOpener.element() as HTMLElement).focus()
  await rootOpener.click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  const modalOpener = view.getByTestId('stack-modal-opener')
  ;(modalOpener.element() as HTMLElement).focus()
  await modalOpener.click()

  const paletteOpener = view.getByTestId('stack-palette-opener')
  await expect.element(paletteOpener).toBeVisible()
  ;(paletteOpener.element() as HTMLElement).focus()
  dispatchShortcut(window)

  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  const paletteInput = view.getByTestId('command-palette-input')

  dispatchKey(paletteInput.element() as HTMLElement, 'Escape')

  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()
  await vi.waitFor(() => {
    expect(view.getByTestId('stack-state').element().textContent).toBe('drawer:true;modal:true;palette:false')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(paletteOpener.element())
  })

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('stack-state').element().textContent).toBe('drawer:true;modal:false;palette:false')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(modalOpener.element())
  })

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('stack-state').element().textContent).toBe('drawer:false;modal:false;palette:false')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(rootOpener.element())
  })
})

test('closes a teleported lookup overlay before the surrounding modal and drawer layers', async () => {
  await page.viewport(1280, 900)

  const view = await render(OverlayLookupHarness)
  const rootOpener = view.getByTestId('overlay-lookup-root-opener')

  ;(rootOpener.element() as HTMLElement).focus()
  await rootOpener.click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  const modalOpener = view.getByTestId('overlay-lookup-modal-opener')
  ;(modalOpener.element() as HTMLElement).focus()
  await modalOpener.click()

  const input = view.getByRole('combobox')
  await input.click()
  await input.fill('cash')
  await expect.element(view.getByRole('option', { name: /1100 Cash/i })).toBeVisible()
  expect(document.querySelector('[role="listbox"]')).not.toBeNull()

  dispatchKey(input.element() as HTMLElement, 'Escape')

  await vi.waitFor(() => {
    expect(document.querySelector('[role="listbox"]')).toBeNull()
  })
  expect(document.activeElement).toBe(input.element())
  expect(view.getByTestId('overlay-lookup-state').element().textContent).toBe('drawer:true;modal:true;lookup:none')

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('overlay-lookup-state').element().textContent).toBe('drawer:true;modal:false;lookup:none')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(modalOpener.element())
  })

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('overlay-lookup-state').element().textContent).toBe('drawer:false;modal:false;lookup:none')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(rootOpener.element())
  })
})
