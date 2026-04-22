import { page } from 'vitest/browser'
import { afterEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import { navigateBack } from '../../../../src/ngb/router/backNavigation'

type MutableHistory = History & {
  length?: number
}

let restoreHistoryLength: (() => void) | null = null

function stubHistoryLength(length: number) {
  const history = window.history as MutableHistory
  const originalDescriptor = Object.getOwnPropertyDescriptor(history, 'length')

  Object.defineProperty(history, 'length', {
    configurable: true,
    value: length,
  })

  restoreHistoryLength = () => {
    if (originalDescriptor) {
      Object.defineProperty(history, 'length', originalDescriptor)
      return
    }

    delete history.length
  }
}

afterEach(() => {
  restoreHistoryLength?.()
  restoreHistoryLength = null
})

const RecordPage = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()

    return () => h('div', [
      h('div', { 'data-testid': 'record-route' }, route.fullPath),
      h('button', {
        type: 'button',
        onClick: () => {
          void navigateBack(router, route, '/documents/pm.invoice?search=open&offset=25')
        },
      }, 'Navigate back'),
    ])
  },
})

const DocumentsPage = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', { 'data-testid': 'documents-route' }, route.fullPath)
  },
})

async function renderBackNavigationHarness(initialRoute: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/records/:id', component: RecordPage },
      { path: '/documents/:documentType', component: DocumentsPage },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(defineComponent({
    setup() {
      return () => h(RouterView)
    },
  }), {
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

test('uses the fallback target in the browser when history is shallow and there is no explicit back target', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderBackNavigationHarness('/records/doc-1')

  stubHistoryLength(1)

  await view.getByRole('button', { name: 'Navigate back' }).click()

  await expect.poll(() => router.currentRoute.value.fullPath).toBe('/documents/pm.invoice?search=open&offset=25')
  await expect.element(view.getByTestId('documents-route')).toHaveTextContent('/documents/pm.invoice?search=open&offset=25')
})
