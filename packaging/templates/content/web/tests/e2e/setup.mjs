import { fileURLToPath } from 'node:url'
import { attachTestEnvironment, startTestEnvironment } from '../../../infrastructure/ngb/environment.mjs'
import { prepareAcceptanceActors, readAcceptanceInventory } from '../../../infrastructure/ngb/acceptance.mjs'

export default async function setup() {
  const root = fileURLToPath(new URL('../../../', import.meta.url))
  if (process.env.NGB_TEST_RUN_ID) {
    await attachTestEnvironment()
    return
  }
  const environment = await startTestEnvironment(root)
  try {
    const inventory = await readAcceptanceInventory(environment)
    process.env.NGB_TEST_ACTORS = JSON.stringify(await prepareAcceptanceActors(environment, inventory))
  } catch (error) {
    await environment.stop()
    throw error
  }
  process.env.NGB_TEST_ORIGIN = environment.origin
  process.env.NGB_TEST_PASSWORD = environment.password
  process.env.NGB_TEST_RUN_ID = environment.runId
  return async () => {
    delete process.env.NGB_TEST_ORIGIN
    delete process.env.NGB_TEST_PASSWORD
    delete process.env.NGB_TEST_RUN_ID
    delete process.env.NGB_TEST_ACTORS
    await environment.stop()
  }
}
