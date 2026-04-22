import { page } from 'vitest/browser'
import { beforeEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { computed, defineComponent, h, ref, watch } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import NgbCommandPaletteDialog from '../../../../src/ngb/command-palette/NgbCommandPaletteDialog.vue'
import { configureNgbCommandPalette } from '../../../../src/ngb/command-palette/config'
import { useCommandPaletteStore } from '../../../../src/ngb/command-palette/store'
import { useCommandPaletteHotkeys } from '../../../../src/ngb/command-palette/useCommandPaletteHotkeys'
import NgbRegisterPageLayout from '../../../../src/ngb/metadata/NgbRegisterPageLayout.vue'
import NgbEntityEditor from '../../../../src/ngb/editor/NgbEntityEditor.vue'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'
import { useMainMenuStore } from '../../../../src/ngb/site/mainMenuStore'

const keyboardPaletteStorageKey = 'ngb:test:platform-keyboard-palette'

function dispatchKey(target: EventTarget, key: string, code = key) {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    code,
    bubbles: true,
    cancelable: true,
  }))
}

function dispatchShortcut(target: Window | HTMLElement) {
  const isMac = /Mac|iPhone|iPad|iPod/i.test(String(navigator.platform ?? ''))

  target.dispatchEvent(new KeyboardEvent('keydown', {
    key: 'k',
    metaKey: isMac,
    ctrlKey: !isMac,
    bubbles: true,
    cancelable: true,
  }))
}

const editorForm = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'display',
              label: 'Display',
              dataType: 'String',
              uiControl: 1,
              isRequired: true,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const DocumentsPage = defineComponent({
  setup() {
    const drawerOpen = ref(false)
    const drawerId = ref<string | null>(null)
    const leaveOpen = ref(false)
    const dirty = ref(false)

    function openEditor(id: string) {
      drawerId.value = id
      drawerOpen.value = true
      dirty.value = true
    }

    function closeEditor() {
      drawerOpen.value = false
      drawerId.value = null
      dirty.value = false
    }

    return () => h('div', { class: 'h-full min-h-0' }, [
      h(NgbRegisterPageLayout, {
        title: 'Documents',
        canBack: false,
        itemsCount: 1,
        total: 1,
        columns: [
          { key: 'number', title: 'Number', width: 220 },
        ],
        rows: [
          { key: 'doc-1', number: 'Invoice INV-001' },
        ],
        storageKey: 'ngb:test:platform-keyboard-grid',
        drawerOpen: drawerOpen.value,
        drawerTitle: '',
        drawerHideHeader: true,
        drawerFlushBody: true,
        onRowActivate: (id: string) => {
          openEditor(String(id))
        },
        'onUpdate:drawerOpen': (value: boolean) => {
          if (!value) closeEditor()
        },
      }, {
        drawerContent: () => drawerOpen.value
          ? h(NgbEntityEditor, {
              kind: 'document',
              mode: 'drawer',
              title: drawerId.value === 'doc-1' ? 'Invoice INV-001' : 'Document',
              loading: false,
              saving: false,
              documentStatusLabel: 'Draft',
              documentStatusTone: 'neutral',
              documentPrimaryActions: [],
              documentMoreActionGroups: [],
              isNew: false,
              isMarkedForDeletion: false,
              form: editorForm,
              model: {
                display: 'Invoice INV-001',
              },
              entityTypeCode: 'pm.invoice',
              status: 1,
              leaveOpen: leaveOpen.value,
              onClose: () => {
                if (dirty.value) {
                  leaveOpen.value = true
                  return
                }

                closeEditor()
              },
              onCancelLeave: () => {
                leaveOpen.value = false
              },
              onConfirmLeave: () => {
                leaveOpen.value = false
                closeEditor()
              },
            })
          : null,
      }),
      h('div', { 'data-testid': 'keyboard-documents-drawer' }, String(drawerOpen.value)),
    ])
  },
})

const ReportsPage = {
  render: () => h('div', { 'data-testid': 'keyboard-reports-page', class: 'p-6' }, 'Reports workspace'),
}

const WorkspaceShell = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()
    const palette = useCommandPaletteStore()
    const menuStore = useMainMenuStore()

    useCommandPaletteHotkeys()
    menuStore.$patch({
      groups: [
        {
          label: 'Workspace',
          ordinal: 0,
          icon: 'home',
          items: [
            { kind: 'page', code: 'documents', label: 'Documents', route: '/workspace/documents', icon: 'file-text', ordinal: 0 },
            { kind: 'page', code: 'reports', label: 'Reports', route: '/workspace/reports', icon: 'bar-chart', ordinal: 1 },
          ],
        },
      ],
    })

    watch(
      () => route.fullPath,
      (fullPath) => {
        palette.setCurrentRoute(fullPath)
      },
      { immediate: true },
    )

    const selectedId = computed(() => route.path.includes('/reports') ? 'reports' : 'documents')

    return () => h('div', { class: 'h-full min-h-0' }, [
      h(NgbSiteShell, {
        moduleTitle: 'Property Management',
        productTitle: 'NGB',
        pageTitle: selectedId.value === 'reports' ? 'Reports' : 'Documents',
        userName: 'Alex Carter',
        userEmail: 'alex.carter@example.com',
        userMeta: 'Administrator',
        userMetaIcon: 'shield-check',
        pinned: [],
        recent: [],
        nodes: [
          { id: 'documents', label: 'Documents', route: '/workspace/documents', icon: 'file-text' },
          { id: 'reports', label: 'Reports', route: '/workspace/reports', icon: 'bar-chart' },
        ],
        settings: [],
        selectedId: selectedId.value,
        onNavigate: (next: string) => {
          void router.push(next)
        },
        onSelect: (_id: string, next: string) => {
          void router.push(next)
        },
        onOpenPalette: () => {
          palette.open()
        },
        onBack: () => undefined,
        onSignOut: () => undefined,
      }, {
        default: () => h('div', { class: 'flex-1 min-h-0 overflow-hidden' }, [h(RouterView)]),
      }),
      h(NgbCommandPaletteDialog),
    ])
  },
})

const AppRoot = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'keyboard-current-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

async function renderKeyboardFlow(initialRoute = '/workspace/documents') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/workspace',
        component: WorkspaceShell,
        children: [
          { path: 'documents', component: DocumentsPage },
          { path: 'reports', component: ReportsPage },
        ],
      },
    ],
  })

  configureNgbCommandPalette({
    router,
    recentStorageKey: keyboardPaletteStorageKey,
    loadReportItems: async () => [],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    router,
    view,
  }
}

beforeEach(() => {
  window.localStorage.removeItem(keyboardPaletteStorageKey)
})

test('supports a keyboard-only platform flow through the register, drawer discard dialog, and command palette', async () => {
  await page.viewport(1440, 900)

  const { view } = await renderKeyboardFlow()

  const viewport = document.querySelector('[tabindex="0"]') as HTMLElement | null
  expect(viewport).not.toBeNull()
  viewport!.focus()

  dispatchKey(viewport!, 'ArrowDown', 'ArrowDown')
  dispatchKey(viewport!, ' ', 'Space')
  dispatchKey(viewport!, 'Enter', 'Enter')
  await expect.element(view.getByTestId('keyboard-documents-drawer')).toHaveTextContent('true')

  await expect.poll(() => (document.activeElement as HTMLElement | null)?.getAttribute('title') ?? '').toBe('Close')
  ;(document.activeElement as HTMLButtonElement).click()

  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()
  const stayButton = view.getByRole('button', { name: 'Stay', exact: true }).element() as HTMLButtonElement
  stayButton.focus()
  stayButton.click()
  await expect.element(view.getByTestId('keyboard-documents-drawer')).toHaveTextContent('true')

  dispatchShortcut(window)
  await expect.element(view.getByRole('combobox')).toBeVisible()

  const paletteInput = view.getByRole('combobox')
  await paletteInput.fill('reports')
  await expect.element(view.getByRole('option', { name: /Reports/i })).toBeVisible()
  dispatchKey(paletteInput.element() as HTMLElement, 'ArrowDown', 'ArrowDown')
  dispatchKey(paletteInput.element() as HTMLElement, 'Enter', 'Enter')

  await expect.element(view.getByTestId('keyboard-reports-page')).toBeVisible()
  await expect.element(view.getByTestId('keyboard-current-route')).toHaveTextContent('/workspace/reports')
})
