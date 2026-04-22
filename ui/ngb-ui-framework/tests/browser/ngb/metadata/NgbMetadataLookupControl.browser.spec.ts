import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'
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

    const behavior = {
      searchLookup: async ({ query }: { query: string }) => {
        if (query.trim().toLowerCase() === 'tower') {
          return [{ id: 'property-2', label: 'Harbor Tower' }]
        }
        return []
      },
      buildLookupTargetUrl: async ({ value, routeFullPath }: { value: string; routeFullPath: string }) => {
        return `/catalogs/pm.property/${value}?from=${encodeURIComponent(routeFullPath)}`
      },
    }

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
