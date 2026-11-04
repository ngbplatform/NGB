import { startTestEnvironment } from '../../../packaging/templates/content/infrastructure/ngb/environment.mjs'

const abort = new AbortController()
process.once('SIGTERM', () => abort.abort())
process.once('SIGINT', () => abort.abort())
const environment = await startTestEnvironment(process.argv[2], { signal: abort.signal })
console.log(JSON.stringify({ runId: environment.runId }))
await new Promise(resolve => {
  const timer = setInterval(() => {}, 1000)
  const stop = () => {
    clearInterval(timer)
    resolve()
  }
  if (abort.signal.aborted) stop()
  else abort.signal.addEventListener('abort', stop, { once: true })
})
await environment.stop()
