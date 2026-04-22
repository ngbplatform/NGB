import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, reactive } from 'vue'

import NgbToastHost from '../../../../src/ngb/primitives/NgbToastHost.vue'
import { provideToasts, type Toast, type ToastApi } from '../../../../src/ngb/primitives/toast'

const toastMocks = vi.hoisted(() => ({
  api: null as ToastApi | null,
  providedApi: null as ToastApi | null,
}))

vi.mock('../../../../src/ngb/primitives/toast', async () => {
  const actual = await vi.importActual<typeof import('../../../../src/ngb/primitives/toast')>(
    '../../../../src/ngb/primitives/toast',
  )

  return {
    ...actual,
    useToasts: () => {
      if (!toastMocks.api) {
        throw new Error('useToasts(): missing mocked toast api')
      }

      return toastMocks.api
    },
  }
})

async function wait(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

function createToastApi(initialToasts: Toast[] = []): ToastApi {
  const state = reactive({
    toasts: [...initialToasts] as Toast[],
  })

  function remove(id: string) {
    state.toasts = state.toasts.filter((toast) => toast.id !== id)
  }

  function push(toast: Omit<Toast, 'id'>) {
    const id = String(Date.now() + Math.random())
    const nextToast: Toast = {
      id,
      tone: 'neutral',
      timeoutMs: 3500,
      ...toast,
    }

    state.toasts = [nextToast, ...state.toasts].slice(0, 5)

    const timeout = nextToast.timeoutMs ?? 0
    if (timeout > 0) {
      window.setTimeout(() => remove(id), timeout)
    }
  }

  return {
    get toasts() {
      return state.toasts
    },
    push,
    remove,
  }
}

const ToastHostHarness = defineComponent({
  setup() {
    return () => h(NgbToastHost)
  },
})

const ToastProviderHarness = defineComponent({
  setup() {
    const toasts = provideToasts()
    toastMocks.providedApi = toasts

    function pushBatch() {
      for (let index = 1; index <= 6; index += 1) {
        toasts.push({
          title: `Toast ${index}`,
          message: `Message ${index}`,
          tone: index % 2 === 0 ? 'success' : 'warn',
          timeoutMs: 0,
        })
      }
    }

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => toasts.push({
          title: 'Auto dismiss',
          message: 'Temporary toast',
          tone: 'danger',
          timeoutMs: 120,
        }),
      }, 'Push auto'),
      h('button', {
        type: 'button',
        onClick: () => toasts.push({
          title: 'Manual toast',
          message: 'Closable toast',
          tone: 'success',
          timeoutMs: 0,
        }),
      }, 'Push manual'),
      h('button', {
        type: 'button',
        onClick: pushBatch,
      }, 'Push batch'),
      h(NgbToastHost),
    ])
  },
})

beforeEach(() => {
  toastMocks.api = createToastApi()
  toastMocks.providedApi = null
})

test('renders injected toasts and removes them when the close action is used', async () => {
  await page.viewport(1280, 900)

  const view = await render(ToastHostHarness)

  toastMocks.api = createToastApi([
    {
      id: 'manual-toast',
      title: 'Manual toast',
      message: 'Closable toast',
      tone: 'success',
      timeoutMs: 0,
    },
  ])

  const refreshedView = await render(ToastHostHarness)
  await expect.element(refreshedView.getByText('Manual toast', { exact: true })).toBeVisible()
  await refreshedView.getByRole('button', { name: 'Close' }).click()
  await wait(0)
  expect(toastMocks.api.toasts).toHaveLength(0)
  expect(document.body.textContent?.includes('Manual toast')).toBe(false)
})

test('exposes a polite live region for toast announcements', async () => {
  await page.viewport(1280, 900)

  toastMocks.api = createToastApi([
    {
      id: 'status-toast',
      title: 'Saved',
      message: 'The record was saved successfully.',
      tone: 'success',
      timeoutMs: 0,
    },
  ])

  const view = await render(ToastHostHarness)
  await expect.element(view.getByText('Saved', { exact: true })).toBeVisible()

  const region = document.querySelector('[aria-label="Notifications"]') as HTMLElement | null
  expect(region).not.toBeNull()
  expect(region?.getAttribute('role')).toBe('region')
  expect(region?.getAttribute('aria-live')).toBe('polite')
  expect(region?.getAttribute('aria-relevant')).toBe('additions text')
})

test('auto-dismisses timed toasts and enforces the five-toast limit in the provider api', async () => {
  await page.viewport(1280, 900)

  const view = await render(ToastProviderHarness)

  await view.getByRole('button', { name: 'Push auto' }).click()
  await wait(20)
  expect(toastMocks.providedApi?.toasts.map((toast) => toast.title)).toContain('Auto dismiss')
  await wait(160)
  expect(toastMocks.providedApi?.toasts.map((toast) => toast.title)).not.toContain('Auto dismiss')

  await view.getByRole('button', { name: 'Push batch' }).click()
  await wait(0)
  expect(toastMocks.providedApi?.toasts.map((toast) => toast.title)).toEqual([
    'Toast 6',
    'Toast 5',
    'Toast 4',
    'Toast 3',
    'Toast 2',
  ])
})
