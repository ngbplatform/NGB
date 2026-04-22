import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import { useRouteQueryEditorDrawer } from '../../../../src/ngb/editor/useRouteQueryEditorDrawer'
import NgbRegisterPageLayout from '../../../../src/ngb/metadata/NgbRegisterPageLayout.vue'
import NgbLookup from '../../../../src/ngb/primitives/NgbLookup.vue'
import NgbSelect from '../../../../src/ngb/primitives/NgbSelect.vue'
import { useToasts } from '../../../../src/ngb/primitives/toast'
import { navigateBack, withBackTarget } from '../../../../src/ngb/router/backNavigation'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

function isVisible(element: Element): boolean {
  let current: Element | null = element

  while (current) {
    const style = window.getComputedStyle(current as HTMLElement)
    if (style.display === 'none' || style.visibility === 'hidden') return false
    current = current.parentElement
  }

  return true
}

function visibleButtonByTitle(title: string): HTMLElement {
  const button = Array.from(document.querySelectorAll(`button[title="${title}"]`)).find(isVisible) as HTMLElement | undefined
  if (!button) throw new Error(`Visible button not found for title: ${title}`)
  return button
}

function visibleButtonByText(label: string): HTMLElement {
  const button = Array.from(document.querySelectorAll('button')).find((entry) => (
    isVisible(entry) && entry.textContent?.trim() === label
  )) as HTMLElement | undefined

  if (!button) throw new Error(`Visible button not found for label: ${label}`)
  return button
}

const HomePage = defineComponent({
  setup() {
    const route = useRoute()
    return () => h('div', { 'data-testid': 'platform-home-page' }, `home:${route.fullPath}`)
  },
})

const DashboardPage = defineComponent({
  setup() {
    return () => h('div', { 'data-testid': 'platform-dashboard-page', class: 'p-6' }, 'Dashboard workspace')
  },
})

const RecordPage = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()

    return () => h('div', { class: 'p-6 space-y-3' }, [
      h('div', { 'data-testid': 'platform-record-page' }, `record:${String(route.params.id ?? 'none')}`),
      h('button', {
        type: 'button',
        onClick: () => {
          void navigateBack(router, route, '/workspace/documents')
        },
      }, 'Record back'),
    ])
  },
})

const DocumentsPage = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()
    const toasts = useToasts()
    const drawer = useRouteQueryEditorDrawer({
      route,
      router,
      clearKeys: ['tab'],
      idImpliesEdit: true,
    })

    const lookupValue = ref<{ id: string; label: string; meta?: string } | null>(null)
    const lookupItems = ref<Array<{ id: string; label: string; meta?: string }>>([])
    const stage = ref<'draft' | 'posted'>('draft')

    const stageOptions = [
      { value: 'draft', label: 'Draft' },
      { value: 'posted', label: 'Posted' },
    ] as const

    const stageLabel = computed(() =>
      stageOptions.find((option) => option.value === stage.value)?.label ?? 'Draft')

    function openCreate() {
      void drawer.openCreateDrawer({ patch: { tab: 'editor' } })
    }

    function openInvoice(id: string) {
      void drawer.openEditDrawer(id, { patch: { tab: 'editor' } })
    }

    function closeDrawer() {
      void drawer.closeDrawer()
    }

    return () => h('div', { class: 'flex-1 min-h-0' }, [
      h('div', { 'data-testid': 'platform-documents-route', class: 'sr-only' }, route.fullPath),
      h(
        NgbRegisterPageLayout,
        {
          title: 'Documents',
          canBack: true,
          itemsCount: 1,
          total: 1,
          showFilter: false,
          disablePrev: true,
          disableNext: true,
          warning: 'Review pending invoices.',
          columns: [{ key: 'number', title: 'Number' }],
          rows: [{ key: 'invoice-1', number: 'INV-001' }],
          storageKey: 'platform-flow:documents',
          drawerOpen: drawer.isPanelOpen.value,
          drawerTitle: drawer.panelMode.value === 'new' ? 'New invoice' : 'Invoice editor',
          drawerSubtitle: drawer.currentId.value ?? 'draft',
          onBack: () => {
            void navigateBack(router, route, '/home?tab=dashboard')
          },
          onCreate: openCreate,
          onRowActivate: (id: string) => {
            openInvoice(String(id))
          },
          'onUpdate:drawerOpen': (value: boolean) => {
            if (!value) closeDrawer()
          },
        },
        {
          grid: () => h('div', { class: 'space-y-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4' }, [
            h('div', { class: 'text-sm text-ngb-muted' }, 'Platform register harness'),
            h('button', {
              type: 'button',
              'data-testid': 'platform-row-invoice-1',
              class: 'rounded-[var(--ngb-radius)] border border-ngb-border px-3 py-2 text-left text-sm ngb-focus',
              onClick: () => {
                openInvoice('invoice-1')
              },
            }, 'Invoice INV-001'),
          ]),
          drawerContent: () => h('div', { class: 'space-y-4 p-4' }, [
            h('div', { 'data-testid': 'platform-editor-mode' }, `mode:${drawer.panelMode.value ?? 'none'};id:${drawer.currentId.value ?? 'new'}`),
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
                onOpen: () => {
                  if (!lookupValue.value) return
                  void router.push(withBackTarget(`/records/${lookupValue.value.id}`, route.fullPath))
                },
              },
            ),
            h(
              NgbSelect,
              {
                modelValue: stage.value,
                label: 'Workflow stage',
                options: stageOptions,
                'onUpdate:modelValue': (value: 'draft' | 'posted') => {
                  stage.value = value
                },
              },
            ),
            h('button', {
              type: 'button',
              'data-testid': 'platform-toast-button',
              class: 'rounded-[var(--ngb-radius)] border border-ngb-border px-3 py-2 text-sm ngb-focus',
              onClick: () => {
                toasts.push({
                  title: 'Editor saved',
                  message: `${lookupValue.value?.label ?? 'No account'} / ${stageLabel.value}`,
                  tone: 'success',
                })
              },
            }, 'Show success toast'),
            h('div', { 'data-testid': 'platform-editor-state' }, `lookup:${lookupValue.value?.label ?? 'none'};stage:${stage.value}`),
          ]),
        },
      ),
    ])
  },
})

const WorkspaceShell = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()
    const selectedId = computed(() => route.path.includes('/documents') ? 'documents' : 'dashboard')

    async function navigateTo(next: string) {
      if (route.fullPath === next) return
      await router.push(next)
    }

    return () => h(
      NgbSiteShell,
      {
        moduleTitle: 'Property Management',
        productTitle: 'NGB',
        pageTitle: selectedId.value === 'documents' ? 'Documents' : 'Dashboard',
        userName: 'Alex Carter',
        userEmail: 'alex.carter@example.com',
        userMeta: 'Administrator',
        userMetaIcon: 'shield-check',
        pinned: [],
        recent: [],
        nodes: [
          { id: 'documents', label: 'Documents', route: '/workspace/documents', icon: 'file-text' },
          { id: 'dashboard', label: 'Dashboard', route: '/workspace/dashboard', icon: 'home' },
        ],
        settings: [],
        selectedId: selectedId.value,
        onNavigate: (next: string) => {
          void navigateTo(next)
        },
        onSelect: (_id: string, next: string) => {
          void navigateTo(next)
        },
        onOpenPalette: () => undefined,
        onBack: () => undefined,
        onSignOut: () => undefined,
      },
      {
        default: () => h('div', { class: 'flex-1 min-h-0 overflow-hidden' }, [h(RouterView)]),
      },
    )
  },
})

const AppRoot = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'platform-app-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

async function renderPlatformFlow(initialRoute: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/home', component: HomePage },
      { path: '/records/:id', component: RecordPage },
      {
        path: '/workspace',
        component: WorkspaceShell,
        children: [
          { path: 'documents', component: DocumentsPage },
          { path: 'dashboard', component: DashboardPage },
        ],
      },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

test('runs a platform flow through shell, list page, drawer editor, lookup/select, toast, and back navigation', async () => {
  await page.viewport(1440, 900)

  const { view } = await renderPlatformFlow(withBackTarget('/workspace/documents', '/home?tab=dashboard'))

  await expect.element(view.getByText('Review pending invoices.', { exact: true })).toBeVisible()

  await visibleButtonByTitle('Create').click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  await expect.element(view.getByTestId('platform-editor-mode')).toHaveTextContent('mode:new;id:new')

  const lookup = view.getByRole('combobox')
  await lookup.click()
  await lookup.fill('cash')
  await expect.element(view.getByRole('option', { name: /1100 Cash/i })).toBeVisible()
  await view.getByRole('option', { name: /1100 Cash/i }).click()
  await expect.element(view.getByTestId('platform-editor-state')).toHaveTextContent('lookup:1100 Cash;stage:draft')

  await view.getByText('Draft', { exact: true }).click()
  await expect.element(view.getByRole('option', { name: 'Posted' })).toBeVisible()
  await view.getByRole('option', { name: 'Posted' }).click()
  await expect.element(view.getByTestId('platform-editor-state')).toHaveTextContent('lookup:1100 Cash;stage:posted')

  await view.getByTestId('platform-toast-button').click()
  await expect.element(view.getByText('Editor saved', { exact: true })).toBeVisible()
  await expect.element(view.getByText('1100 Cash / Posted', { exact: true })).toBeVisible()

  const drawerCloseButton = document.querySelector('[data-testid="drawer-panel"] button[title="Close"]') as HTMLButtonElement | null
  expect(drawerCloseButton).not.toBeNull()
  drawerCloseButton?.click()

  await vi.waitFor(() => {
    expect(document.querySelector('[data-testid="drawer-panel"]')).toBeNull()
  })

  await view.getByRole('button', { name: 'Back' }).click()
  await expect.element(view.getByTestId('platform-home-page')).toHaveTextContent('home:/home?tab=dashboard')
  await expect.element(view.getByTestId('platform-app-route')).toHaveTextContent('/home?tab=dashboard')
})

test('keeps mixed mobile shell and drawer flows stable while resizing up to desktop', async () => {
  await page.viewport(375, 800)

  const { view } = await renderPlatformFlow('/workspace/documents')

  await expect.element(view.getByTestId('site-topbar-main-menu')).toBeVisible()
  await view.getByTestId('site-topbar-main-menu').click()
  await expect.element(view.getByTestId('mobile-main-menu-sidebar')).toBeVisible()

  await visibleButtonByText('Documents').click()
  await vi.waitFor(() => {
    expect(document.querySelector('[data-testid="mobile-main-menu-sidebar"]')).toBeNull()
  })

  await visibleButtonByTitle('Create').click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  const lookup = view.getByRole('combobox')
  await lookup.click()
  await lookup.fill('cash')
  await expect.element(view.getByRole('option', { name: /1100 Cash/i })).toBeVisible()

  await page.viewport(1440, 900)

  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  await expect.element(view.getByRole('option', { name: /1100 Cash/i })).toBeVisible()
  expect(document.activeElement).toBe(lookup.element())

  await view.getByRole('option', { name: /1100 Cash/i }).click()
  await expect.element(view.getByTestId('platform-editor-state')).toHaveTextContent('lookup:1100 Cash;stage:draft')
})
