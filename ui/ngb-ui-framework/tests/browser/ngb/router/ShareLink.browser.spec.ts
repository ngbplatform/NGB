import { page } from 'vitest/browser'
import { afterEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbToastHost from '../../../../src/ngb/primitives/NgbToastHost.vue'
import { provideToasts } from '../../../../src/ngb/primitives/toast'
import { copyAppLink } from '../../../../src/ngb/router/shareLink'

function createHarness(onCopy: (toasts: ReturnType<typeof provideToasts>) => Promise<boolean>) {
  return defineComponent({
    setup() {
    const toasts = provideToasts()

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          void onCopy(toasts)
        },
      }, 'Copy link'),
      h(NgbToastHost),
    ])
    },
  })
}

function mockClipboard(writeText: ReturnType<typeof vi.fn>) {
  Object.defineProperty(globalThis.navigator, 'clipboard', {
    configurable: true,
    value: { writeText },
  })
}

async function renderShareLinkHarness(writeText: ReturnType<typeof vi.fn>) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/home', component: { template: '<div>Home</div>' } },
      { path: '/reports/occupancy', component: { template: '<div>Report</div>' } },
    ],
  })

  await router.push('/home')
  await router.isReady()
  mockClipboard(writeText)

  const view = await render(createHarness(async (toasts) => await copyAppLink(
    router,
    toasts,
    '/reports/occupancy?variant=default',
    {
      title: 'Copied report link',
      message: 'Ready to share.',
    },
  )), {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    router,
    view,
  }
}

afterEach(() => {
  Object.defineProperty(globalThis.navigator, 'clipboard', {
    configurable: true,
    value: undefined,
  })
})

test('copies the absolute app url and surfaces a success toast through the host', async () => {
  await page.viewport(1280, 900)

  const writeText = vi.fn().mockResolvedValue(undefined)
  const { view } = await renderShareLinkHarness(writeText)

  await view.getByRole('button', { name: 'Copy link' }).click()

  expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/reports/occupancy?variant=default`)
  await expect.element(view.getByText('Copied report link')).toBeVisible()
  await expect.element(view.getByText('Ready to share.')).toBeVisible()
})

test('shows a danger toast when the clipboard write fails', async () => {
  await page.viewport(1280, 900)

  const writeText = vi.fn().mockRejectedValue(new Error('Clipboard denied'))
  const { view } = await renderShareLinkHarness(writeText)

  await view.getByRole('button', { name: 'Copy link' }).click()

  await expect.element(view.getByText('Could not copy link')).toBeVisible()
  await expect.element(view.getByText('Clipboard denied')).toBeVisible()
})
