import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { computed, defineComponent, h, watch } from 'vue'
import { RouterView, createMemoryHistory, createRouter, useRoute } from 'vue-router'

import NgbCommandPaletteDialog from '../../../../src/ngb/command-palette/NgbCommandPaletteDialog.vue'
import { configureNgbCommandPalette } from '../../../../src/ngb/command-palette/config'
import { useCommandPaletteHotkeys } from '../../../../src/ngb/command-palette/useCommandPaletteHotkeys'
import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'
import { useEntityEditorCommandPalette } from '../../../../src/ngb/editor/useEntityEditorCommandPalette'
import { useMainMenuStore, type MainMenuGroup } from '../../../../src/ngb/site/mainMenuStore'

const recentStorageKey = 'ngb:test:entity-editor-command-palette:browser'
const invoiceId = '123e4567-e89b-12d3-a456-426614174000'

const editorPaletteMocks = vi.hoisted(() => ({
  openDocumentFlowPage: vi.fn(),
  openDocumentEffectsPage: vi.fn(),
  openDocumentPrintPage: vi.fn(),
  post: vi.fn().mockResolvedValue(undefined),
  unpost: vi.fn().mockResolvedValue(undefined),
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
]

async function wait(ms = 180) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

function dispatchEnter(target: HTMLElement) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key: 'Enter',
    bubbles: true,
    cancelable: true,
  }))
}

beforeEach(() => {
  window.localStorage.removeItem(recentStorageKey)
  vi.clearAllMocks()
})

test('publishes real entity-editor command palette actions and swaps post/unpost by route state', async () => {
  await page.viewport(1280, 900)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/pm.invoice/:id',
        component: defineComponent({
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

            const mode = computed(() => String(route.query.mode ?? '').trim() === 'drawer' ? 'drawer' : 'page')
            const isPosted = computed(() => String(route.query.status ?? '').trim() === 'posted')
            const currentId = computed(() => String(route.params.id ?? '').trim() || null)

            useEntityEditorCommandPalette({
              mode,
              kind: computed(() => 'document'),
              typeCode: computed(() => 'pm.invoice'),
              currentId,
              title: computed(() => isPosted.value ? 'Invoice INV-001 (posted)' : 'Invoice INV-001'),
              canOpenDocumentFlowPage: computed(() => true),
              canOpenEffectsPage: computed(() => true),
              canPrintDocument: computed(() => true),
              canPost: computed(() => !isPosted.value),
              canUnpost: computed(() => isPosted.value),
              openDocumentFlowPage: editorPaletteMocks.openDocumentFlowPage,
              openDocumentEffectsPage: editorPaletteMocks.openDocumentEffectsPage,
              openDocumentPrintPage: editorPaletteMocks.openDocumentPrintPage,
              post: editorPaletteMocks.post,
              unpost: editorPaletteMocks.unpost,
            })

            return () => h('div', { class: 'p-6' }, [
              h('button', {
                type: 'button',
                'data-testid': 'framework-editor-palette-opener',
                onClick: () => palette.open(),
              }, 'Open palette'),
              h('div', { 'data-testid': 'framework-editor-current-route' }, route.fullPath),
              h(NgbCommandPaletteDialog),
            ])
          },
        }),
      },
    ],
  })

  configureNgbCommandPalette({
    router,
    recentStorageKey,
    loadReportItems: async () => [],
  })

  await router.push(`/documents/pm.invoice/${invoiceId}`)
  await router.isReady()

  const view = await render(RouterView, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  await view.getByTestId('framework-editor-palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()
  await expect.element(view.getByRole('option', { name: /Open document flow/i })).toBeVisible()
  await expect.element(view.getByRole('option', { name: /Open accounting effects/i })).toBeVisible()
  await expect.element(view.getByRole('option', { name: /Print document/i })).toBeVisible()

  const input = view.getByTestId('command-palette-input')
  await input.fill('post document')
  const postOption = view.getByRole('option', { name: /Post document/i })
  await expect.element(postOption).toBeVisible()
  ;(postOption.element() as HTMLElement).dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))
  dispatchEnter(input.element() as HTMLElement)
  await wait()

  expect(editorPaletteMocks.post).toHaveBeenCalledTimes(1)
  expect((view.getByTestId('framework-editor-current-route').element().textContent ?? '')).toBe(`/documents/pm.invoice/${invoiceId}`)
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()

  await router.push(`/documents/pm.invoice/${invoiceId}?status=posted`)
  await wait(30)

  await view.getByTestId('framework-editor-palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const postedInput = view.getByTestId('command-palette-input')
  await postedInput.fill('unpost')
  await expect.element(view.getByRole('option', { name: /Unpost document/i })).toBeVisible()
  expect(document.querySelector('[data-item-key^="current:post:"]')).toBeNull()

  dispatchEnter(postedInput.element() as HTMLElement)
  await wait()

  expect(editorPaletteMocks.unpost).toHaveBeenCalledTimes(1)
  await expect.poll(() => document.querySelector('[data-testid="command-palette-dialog"]')).toBeNull()

  await router.push(`/documents/pm.invoice/${invoiceId}?mode=drawer`)
  await wait(30)

  await view.getByTestId('framework-editor-palette-opener').click()
  await expect.element(view.getByTestId('command-palette-dialog')).toBeVisible()

  const drawerInput = view.getByTestId('command-palette-input')
  await drawerInput.fill('flow')
  await wait(30)

  expect(document.querySelector('[data-item-key^="current:flow:"]')).toBeNull()
  expect(document.querySelector('[data-item-key^="current:effects:"]')).toBeNull()
  expect(document.querySelector('[data-item-key^="current:print:"]')).toBeNull()
})
