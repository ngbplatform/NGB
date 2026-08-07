import type { WorkCenterItem } from './types'

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
