import { fileURLToPath } from 'node:url'
import { attachTestEnvironment, startTestEnvironment } from '../../../infrastructure/ngb/environment.mjs'

export default async function setup() {
  const root = fileURLToPath(new URL('../../../', import.meta.url))
  if (process.env.NGB_TEST_RUN_ID) {
    await attachTestEnvironment()
    return
  }
  const environment = await startTestEnvironment(root)
  process.env.NGB_TEST_ORIGIN = environment.origin
  process.env.NGB_TEST_PASSWORD = environment.password
  process.env.NGB_TEST_RUN_ID = environment.runId
  return async () => {
    delete process.env.NGB_TEST_ORIGIN
    delete process.env.NGB_TEST_PASSWORD
    delete process.env.NGB_TEST_RUN_ID
    await environment.stop()
  }
}
