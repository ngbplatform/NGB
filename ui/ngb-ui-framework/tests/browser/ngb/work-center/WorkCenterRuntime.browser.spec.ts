import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import type { NgbWorkCenterConfig } from '../../../../src/ngb/work-center/config'
import {
  provideNgbWorkCenterRuntime,
  type NgbWorkCenterRuntime,
  useNgbWorkCenterRuntime,
  useWorkCenter,
  useWorkCenterPreferences,
} from '../../../../src/ngb/work-center/useWorkCenter'

function testConfig() {
  const unsubscribe = vi.fn()
  const gateway = {
    getSummary: vi.fn(async () => ({
      attentionCount: 0,
      openTaskCount: 0,
      overdueTaskCount: 0,
      notificationCount: 0,
      unreadNotificationCount: 0,
      version: 1,
    })),
    getItems: vi.fn(async () => ({ items: [], nextCursor: null, limit: 30 })),
    markNotificationRead: vi.fn(async () => undefined),
    dismissNotification: vi.fn(async () => undefined),
    markTaskRead: vi.fn(async () => undefined),
    claimTask: vi.fn(async () => undefined),
    snoozeTask: vi.fn(async () => undefined),
    getPreferences: vi.fn(async () => []),
    updatePreferences: vi.fn(async () => undefined),
  }
  const config: NgbWorkCenterConfig = {
    gateway,
    session: {
      getSnapshot: () => ({ authenticated: false, subject: null }),
      getAccessToken: async () => null,
      subscribe: () => unsubscribe,
    },
    createRealtimeClient: () => ({
      start: async () => undefined,
      stop: async () => undefined,
    }),
  }
  return { config, gateway, unsubscribe }
}

test('provides one app-scoped runtime to feeds and preferences and disposes it with the provider', async () => {
  const { config, gateway, unsubscribe } = testConfig()
  let provided: NgbWorkCenterRuntime | null = null
  let injected: NgbWorkCenterRuntime | null = null
  let loadFeed: (() => Promise<void>) | null = null
  let loadPreferences: (() => Promise<unknown>) | null = null
  let savePreferences: (() => Promise<void>) | null = null

  const Consumer = defineComponent({
    setup() {
      const selection = useNgbWorkCenterRuntime()
      const feed = useWorkCenter()
      const preferences = useWorkCenterPreferences()
      injected = selection.runtime
      loadFeed = () => feed.load({ tab: 'attention' })
      loadPreferences = preferences.load
      savePreferences = () => preferences.save([])
      return () => h('span', 'consumer')
    },
  })
  const Provider = defineComponent({
    setup() {
      provided = provideNgbWorkCenterRuntime({ vertical: 'crm', config })
      return () => h(Consumer)
    },
  })

  const view = await render(Provider)
  expect(injected).toBe(provided)
  await loadFeed?.()
  await loadPreferences?.()
  await savePreferences?.()
  expect(gateway.getItems).toHaveBeenCalledWith(
    { tab: 'attention', limit: 30, vertical: 'crm' },
    expect.any(AbortSignal),
  )
  expect(gateway.getPreferences).toHaveBeenCalledTimes(1)
  expect(gateway.updatePreferences).toHaveBeenCalledWith([])

  view.unmount()
  await vi.waitFor(() => expect(unsubscribe).toHaveBeenCalledTimes(1))
})

test('creates an owned runtime when a component has no provider', async () => {
  let ownedRuntime: NgbWorkCenterRuntime | null = null
  const Standalone = defineComponent({
    setup() {
      const selection = useNgbWorkCenterRuntime({ vertical: 'trade' })
      expect(selection.owned).toBe(true)
      ownedRuntime = selection.runtime
      return () => h('span', selection.runtime.vertical)
    },
  })

  const view = await render(Standalone)
  await expect.element(view.getByText('trade')).toBeVisible()
  view.unmount()
  await ownedRuntime?.dispose()
})
