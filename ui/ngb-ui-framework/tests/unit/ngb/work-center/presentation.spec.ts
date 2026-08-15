import { describe, expect, it } from 'vitest'

import {
  formatWorkCenterTimestamp,
  workCenterItemBadge,
  workCenterItemTone,
} from '../../../../src/ngb/work-center/presentation'
import type { WorkCenterItem } from '../../../../src/ngb/work-center/types'

function item(overrides: Partial<WorkCenterItem> = {}): WorkCenterItem {
  return {
    id: 'item-1',
    kind: 'Task',
    code: 'task.code',
    title: 'Review payment',
    source: {
      resourceKind: 'Document',
      resourceCode: 'pm.receivable-payment',
      entityId: 'payment-1',
      title: 'Payment 1',
    },
    sortAtUtc: '2026-07-26T15:30:00.000Z',
    isOverdue: false,
    isRead: false,
    version: 1,
    ...overrides,
  }
}

describe('work center presentation', () => {
  it('formats valid timestamps and safely suppresses empty or invalid timestamps', () => {
    expect(formatWorkCenterTimestamp(null)).toBe('')
    expect(formatWorkCenterTimestamp(undefined)).toBe('')
    expect(formatWorkCenterTimestamp('not-a-date')).toBe('')
    expect(formatWorkCenterTimestamp('2026-07-26T15:30:00.000Z')).not.toBe('')
  })

  it('maps task and notification urgency to semantic color tones', () => {
    expect(workCenterItemTone(item({ isOverdue: true }))).toBe('text-ngb-danger')
    expect(workCenterItemTone(item({ priority: 'Critical' }))).toBe('text-ngb-danger')
    expect(workCenterItemTone(item({ kind: 'Notification', severity: 'Critical' }))).toBe('text-ngb-danger')
    expect(workCenterItemTone(item({ priority: 'High' }))).toBe('text-amber-700 dark:text-amber-300')
    expect(workCenterItemTone(item({ kind: 'Notification', severity: 'Warning' }))).toBe('text-amber-700 dark:text-amber-300')
    expect(workCenterItemTone(item({ kind: 'Notification', severity: 'Success' }))).toBe('text-emerald-700 dark:text-emerald-300')
    expect(workCenterItemTone(item())).toBe('text-ngb-blue')
  })

  it('creates stable badges for tasks and notifications with sensible fallbacks', () => {
    expect(workCenterItemBadge(item({ isOverdue: true, priority: 'Low' }))).toBe('Overdue')
    expect(workCenterItemBadge(item({ priority: 'High' }))).toBe('High')
    expect(workCenterItemBadge(item({ priority: null }))).toBe('Task')
    expect(workCenterItemBadge(item({ kind: 'Notification', severity: 'Success' }))).toBe('Success')
    expect(workCenterItemBadge(item({ kind: 'Notification', severity: null }))).toBe('Notification')
  })
})
