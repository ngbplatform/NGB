import type {
  NotificationChannel,
  NotificationPreference,
  WorkCenterPage,
  WorkCenterQuery,
  WorkCenterSummary,
} from './types'

export type WorkCenterGateway = {
  getSummary: (vertical?: string | null) => Promise<WorkCenterSummary>
  getItems: (query?: WorkCenterQuery, signal?: AbortSignal) => Promise<WorkCenterPage>
  markNotificationRead: (id: string) => Promise<void>
  dismissNotification: (id: string) => Promise<void>
  markTaskRead: (id: string) => Promise<void>
  claimTask: (id: string, expectedVersion: number) => Promise<void>
  snoozeTask: (id: string, snoozedUntilUtc: string) => Promise<void>
  getPreferences: () => Promise<NotificationPreference[]>
  updatePreferences: (
    preferences: Array<{ code: string; channel: NotificationChannel; isEnabled: boolean }>,
  ) => Promise<void>
}
