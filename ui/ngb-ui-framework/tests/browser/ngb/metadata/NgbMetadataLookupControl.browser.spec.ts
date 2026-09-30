import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, reactive, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { StubLookup } from '../accounting/stubs'

vi.mock('../../../../src/ngb/primitives/NgbLookup.vue', () => ({
  default: StubLookup,
}))

import NgbMetadataLookupControl from '../../../../src/ngb/metadata/NgbMetadataLookupControl.vue'

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 60))
}

function queryLookupRoot(index: number): HTMLElement {
  const root = document.querySelectorAll('[data-testid="stub-lookup"]')[index]
  if (!(root instanceof HTMLElement)) throw new Error(`Lookup ${index} not found.`)
  return root
}

async function queryLookup(index: number, value: string) {
  const input = queryLookupRoot(index).querySelector('input')
  if (!(input instanceof HTMLInputElement)) throw new Error(`Lookup ${index} input not found.`)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
}

function clickLookupAction(index: number, action: string) {
  const button = queryLookupRoot(index).querySelector(`button[data-action="${action}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Lookup ${index} action "${action}" not found.`)
  button.click()
}

const ControlHarness = defineComponent({
  setup() {
    const model = ref<{ id: string; display: string } | null>({
      id: 'property-1',
      display: 'Riverfront Tower',
    })
    const returnLookupTarget = ref(true)

    const behavior = reactive<{
      searchLookup: (args: { query: string }) => Promise<Array<{ id: string; label: string }>>
      buildLookupTargetUrl?: (args: { value: string; routeFullPath: string }) => Promise<string | null>
    }>({
      searchLookup: async ({ query }: { query: string }) => {
        if (query.trim().toLowerCase() === 'tower') {
          return [{ id: 'property-2', label: 'Harbor Tower' }]
        }
        return []
      },
      buildLookupTargetUrl: async ({ value, routeFullPath }: { value: string; routeFullPath: string }) => {
        return returnLookupTarget.value
          ? `/catalogs/pm.property/${value}?from=${encodeURIComponent(routeFullPath)}`
          : null
      },
    })

    return () => h('div', [
      h(NgbMetadataLookupControl, {
        hint: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
        modelValue: model.value,
        behavior,
        'onUpdate:modelValue': (next: unknown) => {
          model.value = next as { id: string; display: string } | null
        },
      }),
      h('div', `model:${model.value ? `${model.value.id}:${model.value.display}` : 'none'}`),
      h('button', {
        type: 'button',
        onClick: () => {
          returnLookupTarget.value = false
        },
      }, 'Return no lookup target'),
      h('button', {
        type: 'button',
        onClick: () => {
          behavior.buildLookupTargetUrl = undefined
        },
      }, 'Remove lookup target builder'),
    ])
  },
})

const ReadonlyHarness = defineComponent({
  setup() {
    const model = ref('property-99')

    return () => h('div', [
      h(NgbMetadataLookupControl, {
        hint: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
        modelValue: model.value,
        readonly: true,
        behavior: {},
      }),
      h('div', `model:${model.value}`),
    ])
  },
})

const NoBehaviorHarness = defineComponent({
  setup() {
    return () => h(NgbMetadataLookupControl, {
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      modelValue: {},
      behavior: {},
    })
  },
})

test('searches lookup values, maps selected items into reference objects, opens linked values, and clears selection', async () => {
  await page.viewport(1280, 900)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/edit',
        component: ControlHarness,
      },
      {
        path: '/catalogs/pm.property/:id',
        component: {
          template: '<div>Catalog</div>',
        },
      },
    ],
  })

  await router.push('/documents/edit')
  await router.isReady()

  const view = await render(ControlHarness, {
    global: {
      plugins: [router],
    },
  })

  await expect.element(view.getByText('lookup-value:Riverfront Tower')).toBeVisible()

  await queryLookup(0, 'tower')
  await expect.element(view.getByText('lookup-items:Harbor Tower')).toBeVisible()

  clickLookupAction(0, 'select-first')
  await flushUi()
  await expect.element(view.getByText('model:property-2:Harbor Tower')).toBeVisible()

  clickLookupAction(0, 'open')
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe('/catalogs/pm.property/property-2?from=%2Fdocuments%2Fedit')

  await router.push('/documents/edit')
  await flushUi()

  await view.getByRole('button', { name: 'Return no lookup target' }).click()
  clickLookupAction(0, 'open')
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe('/documents/edit')

  ;(view.getByRole('button', { name: 'Remove lookup target builder' }).element() as HTMLButtonElement).click()
  clickLookupAction(0, 'open')
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe('/documents/edit')

  clickLookupAction(0, 'clear')
  await flushUi()
  await expect.element(view.getByText('model:none')).toBeVisible()
})

test('renders scalar reference ids read-only without exposing clear/open actions when behavior is missing', async () => {
  await page.viewport(1280, 900)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/edit',
        component: ReadonlyHarness,
      },
    ],
  })

  await router.push('/documents/edit')
  await router.isReady()

  const view = await render(ReadonlyHarness, {
    global: {
      plugins: [router],
    },
  })

  await expect.element(view.getByText('lookup-value:property-99')).toBeVisible()
  expect(queryLookupRoot(0).querySelector('button[data-action="open"]')).toBeNull()
  expect(queryLookupRoot(0).querySelector('button[data-action="clear"]')).toBeNull()
})

test('clears search results for empty queries or missing search behavior and ignores unknown values', async () => {
  await page.viewport(1280, 900)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/documents/edit', component: NoBehaviorHarness }],
  })
  await router.push('/documents/edit')
  await router.isReady()

  const view = await render(NoBehaviorHarness, {
    global: {
      plugins: [router],
    },
  })

  await expect.element(view.getByText('lookup-value:none')).toBeVisible()
  await queryLookup(0, '')
  await queryLookup(0, 'unavailable')
  await expect.element(view.getByText('lookup-items:none')).toBeVisible()
})

test('keeps only the latest search result, handles active failures, and aborts on unmount', async () => {
  let search = vi.fn<(args: { query: string; signal: AbortSignal }) => Promise<Array<{ id: string; label: string }>>>()
  const Harness = defineComponent({
    setup() {
      return () => h(NgbMetadataLookupControl, {
        hint: { kind: 'catalog', catalogType: 'pm.property' },
        modelValue: null,
        behavior: { searchLookup: (args: { query: string; signal: AbortSignal }) => search(args) },
      })
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/documents/edit', component: Harness }],
  })
  await router.push('/documents/edit')
  await router.isReady()
  const view = await render(Harness, { global: { plugins: [router] } })

  let resolveSlow!: (items: Array<{ id: string; label: string }>) => void
  search
    .mockImplementationOnce(() => new Promise((resolve) => { resolveSlow = resolve }))
    .mockResolvedValueOnce([{ id: 'new', label: 'Newest result' }])
  const input = queryLookupRoot(0).querySelector('input') as HTMLInputElement
  input.value = 'slow'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.value = 'new'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  resolveSlow([{ id: 'old', label: 'Stale result' }])
  await flushUi()
  await expect.element(view.getByText('lookup-items:Newest result')).toBeVisible()

  search.mockRejectedValueOnce(new Error('lookup unavailable'))
  await queryLookup(0, 'failure')
  await expect.element(view.getByText('lookup-items:none')).toBeVisible()

  let rejectPending!: (cause: unknown) => void
  search.mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectPending = reject }))
  input.value = 'pending'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  const pendingSignal = search.mock.calls.at(-1)?.[0].signal
  view.unmount()
  expect(pendingSignal?.aborted).toBe(true)
  rejectPending(new Error('late lookup failure'))
  await flushUi()
})
