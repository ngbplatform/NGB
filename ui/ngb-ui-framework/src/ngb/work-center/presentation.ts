import { resolveNgbNavigationTarget } from '../navigation/config'
import type { WorkCenterItem, WorkCenterQuery, WorkCenterSummary } from './types'

export type WorkCenterTab = NonNullable<WorkCenterQuery['tab']>

export const workCenterTabs: ReadonlyArray<{ value: WorkCenterTab; label: string }> = [
  { value: 'attention', label: 'Needs Attention' },
  { value: 'tasks', label: 'Tasks' },
  { value: 'notifications', label: 'Notifications' },
  { value: 'completed', label: 'Completed' },
]

export function workCenterTabCount(
  tab: WorkCenterTab,
  summary: WorkCenterSummary | null | undefined,
): number | null {
  switch (tab) {
    case 'attention': return summary?.attentionCount ?? 0
    case 'tasks': return summary?.openTaskCount ?? 0
    case 'notifications': return summary?.notificationCount ?? 0
    default: return null
  }
}

export function resolveWorkCenterItemRoute(item: WorkCenterItem): string | null {
  if (item.target) {
    return resolveNgbNavigationTarget(item.target, {
      resourceKind: item.source.resourceKind,
      resourceCode: item.source.resourceCode,
      entityId: item.source.entityId,
    })
  }
  return item.source.resourceKind.toLowerCase() === 'document'
    ? resolveNgbNavigationTarget({
        code: 'document.editor',
        parameters: {
          documentType: item.source.resourceCode,
          documentId: item.source.entityId,
        },
      }, {
        resourceKind: item.source.resourceKind,
        resourceCode: item.source.resourceCode,
        entityId: item.source.entityId,
      })
    : null
}

export function canClaimWorkCenterItem(item: WorkCenterItem): boolean {
  return item.kind === 'Task'
    && item.taskStatus !== 'Completed'
    && item.taskStatus !== 'Cancelled'
    && !!item.assignment?.isRoleAssigned
    && !item.assignment.claimedByUserId
}

export function canSnoozeWorkCenterItem(item: WorkCenterItem): boolean {
  return item.kind === 'Task'
    && item.taskStatus !== 'Completed'
    && item.taskStatus !== 'Cancelled'
}

export function hasWorkCenterItemActions(item: WorkCenterItem): boolean {
  return item.kind === 'Notification'
    || (item.taskStatus !== 'Completed' && item.taskStatus !== 'Cancelled')
}

export function isWorkCenterItemSnoozed(item: WorkCenterItem, now = Date.now()): boolean {
  return item.kind === 'Task'
    && !!item.snoozedUntilUtc
    && Date.parse(item.snoozedUntilUtc) > now
}

export function formatWorkCenterTimestamp(value: string | null | undefined): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(date)
}

export function workCenterItemTone(item: WorkCenterItem): string {
  if (item.isOverdue || item.priority === 'Critical' || item.severity === 'Critical') return 'text-ngb-danger'
  if (item.priority === 'High' || item.severity === 'Warning') return 'text-amber-700 dark:text-amber-300'
  if (item.severity === 'Success') return 'text-emerald-700 dark:text-emerald-300'
  return 'text-ngb-blue'
}

export function workCenterItemBadge(item: WorkCenterItem): string {
  if (item.kind === 'Task') return item.isOverdue ? 'Overdue' : item.priority ?? 'Task'
  return item.severity ?? 'Notification'
}
