import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref, watch } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import { useRouteQueryEditorDrawer } from '../../../../src/ngb/editor/useRouteQueryEditorDrawer'
import { useRouteLookupSelection, useRouteQueryMigration } from '../../../../src/ngb/router/queryState'

const EXISTING_ID = '11111111-1111-1111-1111-111111111111'
const REVENUE_ID = '22222222-2222-2222-2222-222222222222'
const EDIT_ID = '33333333-3333-3333-3333-333333333333'

const RouteQueryHarnessPage = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()
    const trashMode = ref<'active' | 'deleted'>('active')
    const dirty = ref(false)
    const discardOpen = ref(false)
    let discardResolve: ((value: boolean) => void) | null = null

    function requestDiscard() {
      discardOpen.value = true
      return new Promise<boolean>((resolve) => {
        discardResolve = resolve
      })
    }

    function resolveDiscard(value: boolean) {
      discardOpen.value = false
      if (value) dirty.value = false
      discardResolve?.(value)
      discardResolve = null
    }

    useRouteQueryMigration({
      route,
      router,
      sources: () => [trashMode.value] as const,
      migrate: ([value]) => value === 'deleted' ? { trash: value } : null,
    })

    const drawer = useRouteQueryEditorDrawer({
      route,
      router,
      clearKeys: ['copyDraft'],
      idImpliesEdit: true,
      onBeforeOpen: async (_next, current) => {
        if (current.mode === null || !dirty.value) return true
        return await requestDiscard()
      },
      onBeforeClose: async (current) => {
        if (current.mode === null || !dirty.value) return true
        return await requestDiscard()
      },
    })

    const selection = useRouteLookupSelection({
      route,
      router,
      queryKey: 'accountId',
      lookupById: async (id: string) => {
        if (id === EXISTING_ID) return '1100 Cash'
        if (id === REVENUE_ID) return '4100 Revenue'
        return null
      },
      search: async (query: string) => {
        if (query === 'revenue') {
          return [{ id: REVENUE_ID, label: '4100 Revenue' }]
        }

        return []
      },
      openTarget: async (item) => item ? { path: `/documents/${item.id}` } : null,
    })

    watch(
      () => selection.routeId.value,
      () => {
        void selection.hydrateSelected()
      },
      { immediate: true },
    )

    return () => h('div', [
      h('div', { 'data-testid': 'current-route' }, route.fullPath),
      h('div', { 'data-testid': 'drawer-state' }, `mode:${drawer.panelMode.value ?? 'none'};id:${drawer.currentId.value ?? 'none'}`),
      h('div', { 'data-testid': 'dirty-state' }, `dirty:${String(dirty.value)}`),
      h('div', { 'data-testid': 'discard-state' }, `discard:${String(discardOpen.value)}`),
      h('div', { 'data-testid': 'selection-state' }, `selected:${selection.selected.value?.label ?? 'none'}`),
      h('div', { 'data-testid': 'items-state' }, `items:${selection.items.value.map((item) => item.label).join('|') || 'none'}`),
      h('button', {
        type: 'button',
        onClick: () => {
          trashMode.value = 'deleted'
        },
      }, 'Enable deleted'),
      h('button', {
        type: 'button',
        onClick: () => {
          dirty.value = true
        },
      }, 'Mark dirty'),
      h('button', {
        type: 'button',
        onClick: () => {
          dirty.value = false
        },
      }, 'Mark clean'),
      h('button', {
        type: 'button',
        onClick: () => {
          void drawer.openCreateDrawer({ patch: { tab: 'details' } })
        },
      }, 'Open create drawer'),
      h('button', {
        type: 'button',
        onClick: () => {
          void drawer.openEditDrawer(EDIT_ID, { patch: { source: 'grid' } })
        },
      }, 'Open edit drawer'),
      h('button', {
        type: 'button',
        onClick: () => {
          void drawer.closeDrawer({ patch: { keep: '1' } })
        },
      }, 'Close drawer'),
      h('button', {
        type: 'button',
        onClick: () => {
          void selection.onQuery(' revenue ')
        },
      }, 'Search revenue'),
      h('button', {
        type: 'button',
        onClick: () => {
          selection.onSelect({ id: REVENUE_ID, label: '4100 Revenue' })
        },
      }, 'Select revenue'),
      h('button', {
        type: 'button',
        onClick: () => {
          void selection.openSelected()
        },
      }, 'Open selected'),
      discardOpen.value
        ? h('button', {
          type: 'button',
          onClick: () => {
            resolveDiscard(false)
          },
        }, 'Discard cancel')
        : null,
      discardOpen.value
        ? h('button', {
          type: 'button',
          onClick: () => {
            resolveDiscard(true)
          },
        }, 'Discard confirm')
        : null,
    ])
  },
})

const OpenTargetPage = defineComponent({
  setup() {
    const route = useRoute()
    return () => h('div', { 'data-testid': 'opened-target' }, `opened:${route.params.id ?? 'none'}`)
  },
})

const RouteHarness = defineComponent({
  setup() {
    const route = useRoute()
    return () => h('div', [
      h('div', { 'data-testid': 'shell-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

async function renderRouteHarness(initialRoute: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/workspace',
        component: RouteQueryHarnessPage,
      },
      {
        path: '/documents/:id',
        component: OpenTargetPage,
      },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(RouteHarness, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    router,
    view,
  }
}

test('syncs route-driven drawer, lookup selection, query migration, and open-target navigation in the browser', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderRouteHarness(`/workspace?accountId=${EXISTING_ID}&copyDraft=legacy&id=${EDIT_ID}`)

  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent(`mode:edit;id:${EDIT_ID}`)
  await expect.element(view.getByTestId('selection-state')).toHaveTextContent('selected:1100 Cash')

  await view.getByRole('button', { name: 'Open create drawer' }).click()
  await expect.poll(() => view.getByTestId('current-route').element().textContent ?? '').toContain('panel=new')
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain('tab=details')
  expect(view.getByTestId('current-route').element().textContent ?? '').not.toContain('copyDraft')

  await view.getByRole('button', { name: 'Enable deleted' }).click()
  await expect.poll(() => view.getByTestId('current-route').element().textContent ?? '').toContain('trash=deleted')

  await view.getByRole('button', { name: 'Search revenue' }).click()
  await expect.element(view.getByTestId('items-state')).toHaveTextContent('items:4100 Revenue')

  await view.getByRole('button', { name: 'Select revenue' }).click()
  await expect.poll(() => view.getByTestId('current-route').element().textContent ?? '').toContain(`accountId=${REVENUE_ID}`)
  await expect.element(view.getByTestId('selection-state')).toHaveTextContent('selected:4100 Revenue')

  await view.getByRole('button', { name: 'Open selected' }).click()
  await expect.element(view.getByTestId('opened-target')).toHaveTextContent(`opened:${REVENUE_ID}`)
})

test('replays drawer history with push-based opens and replace-based closes when moving back and forward', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderRouteHarness(`/workspace?accountId=${EXISTING_ID}`)

  await view.getByRole('button', { name: 'Enable deleted' }).click()
  await expect.poll(() => view.getByTestId('current-route').element().textContent ?? '').toContain('trash=deleted')

  await view.getByRole('button', { name: 'Open create drawer' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent('mode:new;id:none')

  await view.getByRole('button', { name: 'Open edit drawer' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent(`mode:edit;id:${EDIT_ID}`)

  await view.getByRole('button', { name: 'Close drawer' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent('mode:none;id:none')
  expect(view.getByTestId('current-route').element().textContent ?? '').not.toContain(`id=${EDIT_ID}`)

  router.back()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:new;id:none')
  })
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain('panel=new')
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain('trash=deleted')

  router.back()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:none;id:none')
  })
  expect(view.getByTestId('current-route').element().textContent ?? '').not.toContain('panel=')
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain('trash=deleted')

  router.forward()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:new;id:none')
  })

  router.forward()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:none;id:none')
  })
  expect(view.getByTestId('current-route').element().textContent ?? '').not.toContain(`id=${EDIT_ID}`)
})

test('guards browser history drawer transitions until dirty changes are confirmed', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderRouteHarness('/workspace')

  await view.getByRole('button', { name: 'Open create drawer' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent('mode:new;id:none')

  await view.getByRole('button', { name: 'Open edit drawer' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent(`mode:edit;id:${EDIT_ID}`)

  await view.getByRole('button', { name: 'Mark dirty' }).click()
  await expect.element(view.getByTestId('dirty-state')).toHaveTextContent('dirty:true')

  router.back()
  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('discard:true')
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent(`mode:edit;id:${EDIT_ID}`)

  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('discard:false')
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent(`mode:edit;id:${EDIT_ID}`)
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain(`panel=edit`)

  router.back()
  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('discard:true')
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:new;id:none')
  })
  expect(view.getByTestId('current-route').element().textContent ?? '').toContain('panel=new')
  await expect.element(view.getByTestId('dirty-state')).toHaveTextContent('dirty:false')

  await view.getByRole('button', { name: 'Mark dirty' }).click()
  await expect.element(view.getByTestId('dirty-state')).toHaveTextContent('dirty:true')

  router.back()
  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('discard:true')
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-state').element().textContent).toBe('mode:none;id:none')
  })
  expect(view.getByTestId('current-route').element().textContent ?? '').not.toContain('panel=')
})
