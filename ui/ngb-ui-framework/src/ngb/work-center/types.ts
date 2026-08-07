import type { NgbNavigationTarget } from '../navigation/types'

export type WorkCenterItemKind = 'Task' | 'Notification'
export type WorkCenterTaskStatus = 'Open' | 'InProgress' | 'Completed' | 'Cancelled'
export type WorkCenterPriority = 'Low' | 'Normal' | 'High' | 'Critical'
export type NotificationSeverity = 'Information' | 'Success' | 'Warning' | 'Critical'
export type NotificationChannel = 'InApp'
export type WorkCenterPreferenceKind = 'Task' | 'Notification'

export type WorkCenterSummary = {
  attentionCount: number
  openTaskCount: number
  overdueTaskCount: number
  notificationCount: number
  unreadNotificationCount: number
  version: number
}

export type WorkCenterSource = {
  resourceKind: string
  resourceCode: string
  entityId: string
  title: string
  subtitle?: string | null
}

export type WorkCenterAssignment = {
  assignedUserId?: string | null
  assignedRoleId?: string | null
  claimedByUserId?: string | null
  isRoleAssigned: boolean
}

export type WorkCenterItem = {
  id: string
  kind: WorkCenterItemKind
  code: string
  title: string
  description?: string | null
  source: WorkCenterSource
  priority?: WorkCenterPriority | null
  severity?: NotificationSeverity | null
  taskStatus?: WorkCenterTaskStatus | null
  sortAtUtc: string
  dueAtUtc?: string | null
  isOverdue: boolean
  isRead: boolean
  snoozedUntilUtc?: string | null
  assignment?: WorkCenterAssignment | null
  primaryActionCode?: string | null
  target?: NgbNavigationTarget | null
  version: number
}

export type WorkCenterPage = {
  items: WorkCenterItem[]
  nextCursor?: string | null
  limit: number
}

export type WorkCenterQuery = {
  cursor?: string | null
  limit?: number
  tab?: 'attention' | 'tasks' | 'notifications' | 'completed' | null
  vertical?: string | null
  priority?: WorkCenterPriority | null
  severity?: NotificationSeverity | null
  overdue?: boolean | null
  unread?: boolean | null
}

export type NotificationPreference = {
  code: string
  kind: WorkCenterPreferenceKind
  displayName: string
  category: string
  description?: string | null
  channel: NotificationChannel
  isEnabled: boolean
  defaultEnabled: boolean
  userCanDisable: boolean
  isMandatory: boolean
}
