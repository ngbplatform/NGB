import '../../src/styles/tailwind.css'

import { vi } from 'vitest'

// Component tests start with deployment features disabled; content tests opt in explicitly.
vi.mock('../../src/ngb/features/api', () => ({
  getFeatures: async () => [],
}))

import { configureNgbWorkCenter } from '../../src/ngb/work-center/config'

configureNgbWorkCenter({
  gateway: {
    getSummary: async () => ({
      attentionCount: 0,
      openTaskCount: 0,
      overdueTaskCount: 0,
      notificationCount: 0,
      unreadNotificationCount: 0,
      version: 0,
    }),
    getItems: async () => ({ items: [], nextCursor: null }),
    markNotificationRead: async () => undefined,
    dismissNotification: async () => undefined,
    markTaskRead: async () => undefined,
    claimTask: async () => undefined,
    snoozeTask: async () => undefined,
    getPreferences: async () => [],
    updatePreferences: async () => undefined,
  },
  session: {
    getSnapshot: () => ({ authenticated: false, subject: null }),
    getAccessToken: async () => null,
    subscribe: () => () => undefined,
  },
  createRealtimeClient: () => ({
    start: async () => undefined,
    stop: async () => undefined,
  }),
})
