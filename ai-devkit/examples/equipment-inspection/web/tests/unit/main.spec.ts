import { expect, it, vi } from 'vitest'
import { startApplication } from '../../src/bootstrap'

vi.mock('../../src/bootstrap', () => ({ startApplication: vi.fn().mockResolvedValue(undefined) }))

it('starts the application once from the browser entry point', async () => {
  await import('../../src/main')
  expect(startApplication).toHaveBeenCalledOnce()
})
