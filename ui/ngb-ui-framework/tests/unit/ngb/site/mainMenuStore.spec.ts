import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: mocks.httpGet,
}))

import { useMainMenuStore } from '../../../../src/ngb/site/mainMenuStore'

describe('mainMenuStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('loads main menu groups successfully', async () => {
    mocks.httpGet.mockResolvedValue({
      groups: [
        {
          label: 'Operations',
          ordinal: 10,
          icon: 'building',
          items: [
            {
              kind: 'page',
              code: 'properties',
              label: 'Properties',
              route: '/properties',
              ordinal: 0,
            },
          ],
        },
      ],
    })

    const store = useMainMenuStore()
    await store.load()

    expect(mocks.httpGet).toHaveBeenCalledWith('/api/main-menu')
    expect(store.groups).toEqual([
      {
        label: 'Operations',
        ordinal: 10,
        icon: 'building',
        items: [
          {
            kind: 'page',
            code: 'properties',
            label: 'Properties',
            route: '/properties',
            ordinal: 0,
          },
        ],
      },
    ])
    expect(store.error).toBeNull()
    expect(store.isLoading).toBe(false)
  })

  it('normalizes an empty response to an empty group collection', async () => {
    mocks.httpGet.mockResolvedValue(null)
    const store = useMainMenuStore()

    await store.load()

    expect(store.groups).toEqual([])
    expect(store.error).toBeNull()
    expect(store.isLoading).toBe(false)
  })

  it('reuses a successful load until forced or reset', async () => {
    mocks.httpGet.mockResolvedValue({ groups: [] })
    const store = useMainMenuStore()

    await Promise.all([store.load(), store.load()])
    await store.load()
    expect(mocks.httpGet).toHaveBeenCalledTimes(1)

    await store.load(true)
    expect(mocks.httpGet).toHaveBeenCalledTimes(2)

    store.reset()
    expect(store.hasLoaded).toBe(false)
    await store.load()
    expect(mocks.httpGet).toHaveBeenCalledTimes(3)
  })

  it('clears groups, stores a friendly error, and logs the failure', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    mocks.httpGet.mockRejectedValue(new Error('Menu API offline'))

    const store = useMainMenuStore()
    store.groups = [
      {
        label: 'Old',
        ordinal: 0,
        items: [],
      },
    ]

    await store.load()

    expect(store.groups).toEqual([])
    expect(store.error).toBe('Menu API offline')
    expect(store.isLoading).toBe(false)
    expect(consoleError).toHaveBeenCalled()

    consoleError.mockRestore()
  })
})
