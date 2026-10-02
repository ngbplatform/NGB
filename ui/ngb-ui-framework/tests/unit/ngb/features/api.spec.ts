import { beforeEach, expect, it, vi } from 'vitest'
import { ApiError, httpGet } from '../../../../src/ngb/api/http'
import { getFeatures } from '../../../../src/ngb/features/api'

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAccessToken: vi.fn(),
  forceRefreshAccessToken: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', async importOriginal => ({
  ...await importOriginal<typeof import('../../../../src/ngb/api/http')>(),
  httpGet: vi.fn(),
}))

beforeEach(() => vi.resetAllMocks())

it('uses authenticated platform transport for feature discovery', async () => {
  vi.mocked(httpGet).mockResolvedValue([])
  expect(await getFeatures()).toEqual([])
  expect(httpGet).toHaveBeenCalledWith('/api/features')
})

it('treats a 3.0 host without discovery as having no enabled features', async () => {
  vi.mocked(httpGet).mockRejectedValue(new ApiError({ message: 'Not found', status: 404, url: '/api/features' }))
  expect(await getFeatures()).toEqual([])
})

it('does not conceal authentication or server failures as a legacy host', async () => {
  const error = new ApiError({ message: 'Unauthorized', status: 401, url: '/api/features' })
  vi.mocked(httpGet).mockRejectedValue(error)
  await expect(getFeatures()).rejects.toBe(error)
})

it('propagates transport failures for the store to report and retry', async () => {
  const error = new Error('Connection refused')
  vi.mocked(httpGet).mockRejectedValue(error)

  await expect(getFeatures()).rejects.toBe(error)
})
