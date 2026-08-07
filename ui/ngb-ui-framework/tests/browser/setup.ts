import '../../src/styles/tailwind.css'

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
