import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { useFeatureStore } from '../../../../src/ngb/features/useFeatureStore'
import { getFeatures } from '../../../../src/ngb/features/api'
import type { FeatureState } from '../../../../src/ngb/features/types'

vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))

beforeEach(() => {
  vi.resetAllMocks()
  setActivePinia(createPinia())
})

afterEach(() => {
  vi.useRealTimers()
})

it('defaults off and exposes only server-enabled features', async () => {
  const store = useFeatureStore()
  expect(store.isEnabled('Attachments')).toBe(false)
  vi.mocked(getFeatures).mockResolvedValue([
    { code: 'Attachments', displayName: 'Attachments', group: 'Content', enabled: false },
    { code: 'Notes', displayName: 'Notes', group: 'Content', enabled: true },
  ])

  await store.load()

  expect(store.isEnabled('Attachments')).toBe(false)
  expect(store.isEnabled('Notes')).toBe(true)
  expect(store.isEnabled('Unknown')).toBe(false)
  expect(store.loading).toBe(false)
  expect(store.error).toBe('')
})

it('deduplicates concurrent loads, caches briefly and supports explicit refresh', async () => {
  let resolve!: (value: FeatureState[]) => void
  vi.mocked(getFeatures).mockReturnValue(new Promise(value => {
    resolve = value
  }))
  const store = useFeatureStore()
  const first = store.load()
  const concurrent = store.load()
  expect(getFeatures).toHaveBeenCalledTimes(1)
  expect(store.loading).toBe(true)
  resolve([])
  await Promise.all([first, concurrent])
  await store.load()
  expect(getFeatures).toHaveBeenCalledTimes(1)
  await store.load(true)
  expect(getFeatures).toHaveBeenCalledTimes(2)
})

it('fails closed on refresh errors and allows retry', async () => {
  const store = useFeatureStore()
  vi.mocked(getFeatures).mockResolvedValue([{ code: 'Notes', displayName: 'Notes', group: 'Content', enabled: true }])
  await store.load()
  vi.mocked(getFeatures).mockRejectedValue(new Error('Unavailable'))
  await store.load(true)
  expect(store.isEnabled('Notes')).toBe(false)
  expect(store.error).toBe('Unavailable')
  vi.mocked(getFeatures).mockResolvedValue([])
  await store.load()
  expect(store.error).toBe('')
})

it('reset discards late responses from the previous session', async () => {
  let resolve!: (value: FeatureState[]) => void
  vi.mocked(getFeatures).mockReturnValue(new Promise(value => {
    resolve = value
  }))
  const store = useFeatureStore()
  const pending = store.load()
  store.reset()
  resolve([{ code: 'Notes', displayName: 'Notes', group: 'Content', enabled: true }])
  await pending
  expect(store.current).toEqual([])
  expect(store.loading).toBe(false)
  expect(store.isEnabled('Notes')).toBe(false)
})

it('refreshes expired discovery state', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-10-02T12:00:00Z'))
  vi.mocked(getFeatures).mockResolvedValue([])
  const store = useFeatureStore()
  await store.load()

  vi.advanceTimersByTime(30001)
  await store.load()

  expect(getFeatures).toHaveBeenCalledTimes(2)
})

it('ignores a stale session failure while a new discovery request is pending', async () => {
  let rejectPrevious!: (reason: Error) => void
  let resolveCurrent!: (value: FeatureState[]) => void
  vi.mocked(getFeatures)
    .mockReturnValueOnce(new Promise((_, reject) => {
      rejectPrevious = reject
    }))
    .mockReturnValueOnce(new Promise(resolve => {
      resolveCurrent = resolve
    }))
  const store = useFeatureStore()
  const previous = store.load()
  store.reset()
  const current = store.load()

  rejectPrevious(new Error('Previous session failed'))
  await previous
  expect(store.loading).toBe(true)
  expect(store.error).toBe('')

  resolveCurrent([])
  await current
  expect(store.loading).toBe(false)
})
