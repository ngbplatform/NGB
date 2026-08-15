import { httpGet, httpPost, httpPut } from '../api/http'
import type {
  NotificationChannel,
  NotificationPreference,
  WorkCenterPage,
  WorkCenterQuery,
  WorkCenterSummary,
} from './types'
import type { WorkCenterGateway } from './gateway'

export function getWorkCenterSummary(vertical?: string | null): Promise<WorkCenterSummary> {
  return httpGet<WorkCenterSummary>('/api/work-center/summary', { vertical })
}

export function getWorkCenterItems(query: WorkCenterQuery = {}, signal?: AbortSignal): Promise<WorkCenterPage> {
  return httpGet<WorkCenterPage>('/api/work-center/items', {
    cursor: query.cursor,
    limit: query.limit ?? 30,
    tab: query.tab,
    vertical: query.vertical,
    priority: query.priority,
    severity: query.severity,
    overdue: query.overdue,
    unread: query.unread,
  }, { signal })
}

export function markWorkCenterNotificationRead(id: string): Promise<void> {
  return httpPost<void>(`/api/work-center/notifications/${encodeURIComponent(id)}/read`)
}

export function dismissWorkCenterNotification(id: string): Promise<void> {
  return httpPost<void>(`/api/work-center/notifications/${encodeURIComponent(id)}/dismiss`)
}

export function markWorkCenterTaskRead(id: string): Promise<void> {
  return httpPost<void>(`/api/work-center/tasks/${encodeURIComponent(id)}/read`)
}

export function claimWorkCenterTask(id: string, expectedVersion: number): Promise<void> {
  return httpPost<void>(`/api/work-center/tasks/${encodeURIComponent(id)}/claim`, { expectedVersion })
}

export function snoozeWorkCenterTask(id: string, snoozedUntilUtc: string): Promise<void> {
  return httpPost<void>(`/api/work-center/tasks/${encodeURIComponent(id)}/snooze`, { snoozedUntilUtc })
}

export function getNotificationPreferences(): Promise<NotificationPreference[]> {
  return httpGet<NotificationPreference[]>('/api/me/notification-preferences')
}

export function updateNotificationPreferences(
  preferences: Array<{ code: string; channel: NotificationChannel; isEnabled: boolean }>,
): Promise<void> {
  return httpPut<void>('/api/me/notification-preferences', { preferences })
}

export const workCenterHttpGateway: WorkCenterGateway = {
  getSummary: getWorkCenterSummary,
  getItems: getWorkCenterItems,
  markNotificationRead: markWorkCenterNotificationRead,
  dismissNotification: dismissWorkCenterNotification,
  markTaskRead: markWorkCenterTaskRead,
  claimTask: claimWorkCenterTask,
  snoozeTask: snoozeWorkCenterTask,
  getPreferences: getNotificationPreferences,
  updatePreferences: updateNotificationPreferences,
}
