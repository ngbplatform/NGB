import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { setTimeout as delay } from 'node:timers/promises'

export function transientDownloadFailure(message) {
  return /\b(?:408|429|500|502|503|504)\b|ETIMEDOUT|ECONNRESET|EAI_AGAIN|ENOTFOUND|UND_ERR_(?:CONNECT_TIMEOUT|SOCKET)|TimeoutError|socket hang up|TLS handshake timeout|connection reset|temporary failure|i\/o timeout/i.test(message)
    && !/unauthorized|denied|manifest unknown|not found|checksum|integrity/i.test(message)
}

export async function retryDownload(operation, { attempts = 3, wait = delay, log = console.warn } = {}) {
  assert.ok(Number.isInteger(attempts) && attempts >= 1 && attempts <= 3, 'Download retry budget must be between one and three attempts.')
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await operation()
    } catch (error) {
      if (error.code === 'ABORT_ERR' || error.name === 'AbortError') throw error
      if (attempt >= attempts || !transientDownloadFailure(`${error.name}: ${error.message} ${error.code ?? ''} ${error.cause?.code ?? ''}`)) throw error
      const milliseconds = 1000 * 2 ** (attempt - 1)
      log(`Temporary download failure; retry ${attempt + 1}/${attempts} in ${milliseconds / 1000}s.`)
      await wait(milliseconds)
    }
  }
}

export async function command(executable, args, { cwd, env = process.env, capture = false, timeout = 90 * 60_000 } = {}) {
  return await new Promise((resolve, reject) => {
    const grouped = process.platform !== 'win32'
    const child = spawn(executable, args, { cwd, env, detached: grouped, stdio: ['ignore', 'pipe', 'pipe'] })
    let output = ''
    let interrupted = false
    let timedOut = false
    let killTimer
    const kill = signal => {
      try {
        if (grouped) process.kill(-child.pid, signal)
        else child.kill(signal)
      } catch (error) {
        if (error.code !== 'ESRCH') throw error
      }
    }
    const stop = signal => {
      interrupted = true
      kill(signal)
      killTimer ??= setTimeout(() => kill('SIGKILL'), 45_000)
      killTimer.unref()
    }
    const interrupt = () => stop('SIGINT')
    const terminate = () => stop('SIGTERM')
    process.once('SIGINT', interrupt)
    process.once('SIGTERM', terminate)
    const timer = setTimeout(() => {
      timedOut = true
      stop('SIGTERM')
    }, timeout)
    timer.unref()
    for (const [stream, destination] of [[child.stdout, process.stdout], [child.stderr, process.stderr]]) {
      stream.on('data', chunk => {
        output = (output + chunk.toString()).slice(-32 * 1024 * 1024)
        if (!capture) destination.write(chunk)
      })
    }
    const cleanup = () => {
      clearTimeout(timer)
      clearTimeout(killTimer)
      process.removeListener('SIGINT', interrupt)
      process.removeListener('SIGTERM', terminate)
    }
    child.once('error', error => {
      cleanup()
      reject(error)
    })
    child.once('close', (code, signal) => {
      cleanup()
      if (code === 0 && !interrupted) {
        resolve(output.trim())
      } else {
        const failure = new Error(`${executable} failed (${signal ?? code}${interrupted ? ', interrupted' : ''}).\n${output.slice(-8000)}`)
        if (interrupted) failure.code = timedOut ? 'ETIMEDOUT' : 'ABORT_ERR'
        reject(failure)
      }
    })
  })
}
