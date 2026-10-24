import { httpGet, httpPostStream, InvalidStreamResponseError } from '../api/http'
import type { AssistantEvent, AssistantRequest, AssistantSettings } from './types'

const maximumEventCharacters = 64 * 1024

export function getAssistantSettings(signal?: AbortSignal): Promise<AssistantSettings> {
  return httpGet<AssistantSettings>('/api/ai-assistant', undefined, { signal })
}

export async function streamAssistant(
  request: AssistantRequest,
  onEvent: (event: AssistantEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  let response: Response
  try {
    response = await httpPostStream('/api/ai-assistant/chat', request, { signal })
  } catch (cause) {
    if (cause instanceof InvalidStreamResponseError) {
      throw new Error('The assistant returned an invalid response.', { cause })
    }
    throw cause
  }

  const reader = response.body!.getReader()
  const decoder = new TextDecoder()
  let pending = ''
  let completed = false

  try {
    while (!completed) {
      signal.throwIfAborted()
      const { value, done } = await reader.read()
      pending += decoder.decode(value, { stream: !done })
      let boundary: number
      while ((boundary = pending.indexOf('\n\n')) >= 0) {
        const frame = pending.slice(0, boundary)
        pending = pending.slice(boundary + 2)
        if (frame.length > maximumEventCharacters) throw new Error('The assistant response is too large.')
        const data = frame.split('\n').filter(line => line.startsWith('data:')).map(line => line.slice(5).trimStart()).join('\n')
        if (!data) continue
        const event = JSON.parse(data) as AssistantEvent
        if (!event || !['text', 'status', 'source', 'value', 'heartbeat', 'done', 'error'].includes(event.type)
          || (event.text != null && typeof event.text !== 'string')) {
          throw new Error('The assistant returned an invalid event.')
        }
        if (event.type === 'value' && (!event.value
          || !/^\{\{ngb-value:[a-f0-9]{12}-\d{1,3}\}\}$/.test(event.value.reference)
          || !['date', 'timestamp', 'bytes', 'text'].includes(event.value.kind)
          || typeof event.value.value !== 'string' || event.value.value.length > 8000)) {
          throw new Error('The assistant returned an invalid display value.')
        }
        if (event.type === 'error') throw new Error(event.text || 'The assistant could not complete the request.')
        onEvent(event)
        if (event.type === 'done') {
          completed = true
          break
        }
      }
      if (pending.length > maximumEventCharacters) throw new Error('The assistant response is too large.')
      if (done) break
    }
    if (!completed) throw new Error('The assistant connection ended before the answer was complete.')
  } finally {
    await reader.cancel().catch(() => undefined)
    reader.releaseLock()
  }
}
