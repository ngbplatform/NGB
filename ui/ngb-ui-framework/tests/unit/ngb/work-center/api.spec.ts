import { beforeEach, describe, expect, it, vi } from 'vitest'

const http = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: http.get,
  httpPost: http.post,
  httpPut: http.put,
}))

import {
  claimWorkCenterTask,
  dismissWorkCenterNotification,
  getNotificationPreferences,
  getWorkCenterItems,
  getWorkCenterSummary,
  markWorkCenterNotificationRead,
  markWorkCenterTaskRead,
  snoozeWorkCenterTask,
  updateNotificationPreferences,
} from '../../../../src/ngb/work-center/api'

describe('work center api', () => {
  beforeEach(() => {
    http.get.mockReset()
    http.post.mockReset()
    http.put.mockReset()
  })

  it('uses cursor paging and explicit task filters', async () => {
    http.get.mockResolvedValueOnce({ items: [], nextCursor: null, limit: 30 })

    await getWorkCenterItems({
      cursor: 'cursor-1',
      tab: 'tasks',
      priority: 'High',
      overdue: true,
    })

    expect(http.get).toHaveBeenCalledWith('/api/work-center/items', {
      cursor: 'cursor-1',
      limit: 30,
      tab: 'tasks',
      vertical: undefined,
      priority: 'High',
      severity: undefined,
      overdue: true,
      unread: undefined,
    }, { signal: undefined })

    const signal = new AbortController().signal
    await getWorkCenterItems({
      limit: 50,
      tab: 'notifications',
      vertical: 'crm',
      severity: 'Warning',
      unread: true,
    }, signal)

    expect(http.get).toHaveBeenLastCalledWith('/api/work-center/items', {
      cursor: undefined,
      limit: 50,
      tab: 'notifications',
      vertical: 'crm',
      priority: undefined,
      severity: 'Warning',
      overdue: undefined,
      unread: true,
    }, { signal })
  })

  it('uses defaults and exposes summary and preference reads', async () => {
    http.get
      .mockResolvedValueOnce({ items: [], nextCursor: null, limit: 30 })
      .mockResolvedValueOnce({
        attentionCount: 0,
        openTaskCount: 0,
        overdueTaskCount: 0,
        notificationCount: 0,
        unreadNotificationCount: 0,
        version: 1,
      })
      .mockResolvedValueOnce({
        attentionCount: 0,
        openTaskCount: 0,
        overdueTaskCount: 0,
        notificationCount: 0,
        unreadNotificationCount: 0,
        version: 1,
      })
      .mockResolvedValueOnce([])

    await getWorkCenterItems()
    await getWorkCenterSummary()
    await getWorkCenterSummary('crm')
    await getNotificationPreferences()

    expect(http.get).toHaveBeenNthCalledWith(1, '/api/work-center/items', {
      cursor: undefined,
      limit: 30,
      tab: undefined,
      vertical: undefined,
      priority: undefined,
      severity: undefined,
      overdue: undefined,
      unread: undefined,
    }, { signal: undefined })
    expect(http.get).toHaveBeenNthCalledWith(2, '/api/work-center/summary', {
      vertical: undefined,
    })
    expect(http.get).toHaveBeenNthCalledWith(3, '/api/work-center/summary', {
      vertical: 'crm',
    })
    expect(http.get).toHaveBeenNthCalledWith(4, '/api/me/notification-preferences')
  })

  it('sends task, notification, and preference mutations through encoded resource routes', async () => {
    http.post.mockResolvedValue(undefined)
    http.put.mockResolvedValue(undefined)

    await markWorkCenterNotificationRead('notification/1')
    await dismissWorkCenterNotification('notification/1')
    await markWorkCenterTaskRead('task/1')
    await claimWorkCenterTask('task/1', 8)
    await snoozeWorkCenterTask('task/1', '2026-07-27T15:00:00.000Z')
    await updateNotificationPreferences([{
      code: 'crm.qualify_lead',
      channel: 'InApp',
      isEnabled: false,
    }])

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/api/work-center/notifications/notification%2F1/read',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/api/work-center/notifications/notification%2F1/dismiss',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      3,
      '/api/work-center/tasks/task%2F1/read',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      4,
      '/api/work-center/tasks/task%2F1/claim',
      { expectedVersion: 8 },
    )
    expect(http.post).toHaveBeenNthCalledWith(
      5,
      '/api/work-center/tasks/task%2F1/snooze',
      { snoozedUntilUtc: '2026-07-27T15:00:00.000Z' },
    )
    expect(http.put).toHaveBeenCalledWith('/api/me/notification-preferences', {
      preferences: [{
        code: 'crm.qualify_lead',
        channel: 'InApp',
        isEnabled: false,
      }],
    })
  })
})
