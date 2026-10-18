import { beforeEach, expect, it, vi } from 'vitest'
import { ApiError, httpPostStream, InvalidStreamResponseError } from '../../../../src/ngb/api/http'
import { streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import type { AssistantEvent } from '../../../../src/ngb/ai-assistant/types'

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAccessToken: vi.fn(),
  forceRefreshAccessToken: vi.fn(),
}))
vi.mock('../../../../src/ngb/api/http', async importOriginal => ({
  ...await importOriginal<typeof import('../../../../src/ngb/api/http')>(),
  httpGet: vi.fn(),
  httpPostStream: vi.fn(),
}))

beforeEach(() => vi.resetAllMocks())

function response(text: string, chunkSize = 1): void {
  const bytes = new TextEncoder().encode(text)
  vi.mocked(httpPostStream).mockResolvedValue(new Response(new ReadableStream({
    start(controller) {
      for (let index = 0; index < bytes.length; index += chunkSize) {
        controller.enqueue(bytes.slice(index, index + chunkSize))
      }
      controller.close()
    },
  })))
}

it('parses fragmented UTF-8 and waits for the terminal event', async () => {
  response(': heartbeat\n\ndata: {"type":"text","text":"Привет 👋"}\n\ndata: {"type":"done"}\n\n')
  const events: AssistantEvent[] = []

  await streamAssistant({ messages: [{ role: 'user', text: 'Hi' }] }, event => events.push(event), new AbortController().signal)

  expect(events).toEqual([{ type: 'text', text: 'Привет 👋' }, { type: 'done' }])
})

it.each([
  ['data: {"type":"text","text":"Partial"}\n\n', 'before the answer was complete'],
  ['data: {"type":"error","text":"Unavailable"}\n\n', 'Unavailable'],
  ['data: {"type":"unexpected"}\n\n', 'invalid event'],
  ['data: {"type":"text","text":4}\n\n', 'invalid event'],
  ['data: {"type":"value","value":{"reference":"forged","kind":"text","value":"hi"}}\n\n', 'invalid display value'],
  ['data: {"type":"value","value":{"reference":"{{ngb-value:abcdef012345-0}}","kind":"html","value":"<script>"}}\n\n', 'invalid display value'],
  ['x'.repeat(65537), 'too large'],
])('rejects incomplete or invalid streams', async (text, message) => {
  response(text, 4096)

  await expect(streamAssistant({ messages: [{ role: 'user', text: 'Hi' }] }, vi.fn(), new AbortController().signal))
    .rejects.toThrow(message)
})

it('cancels the reader when the consumer rejects output', async () => {
  const cancel = vi.fn()
  vi.mocked(httpPostStream).mockResolvedValue(new Response(new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode('data: {"type":"text","text":"Hello"}\n\n'))
    },
    cancel,
  })))

  await expect(streamAssistant({ messages: [{ role: 'user', text: 'Hi' }] }, () => {
    throw new Error('limit')
  }, new AbortController().signal)).rejects.toThrow('limit')

  expect(cancel).toHaveBeenCalledOnce()
})

it('maps only the transport format error to an assistant message', async () => {
  const cause = new InvalidStreamResponseError()
  vi.mocked(httpPostStream).mockRejectedValue(cause)

  await expect(streamAssistant({ messages: [{ role: 'user', text: 'Hi' }] }, vi.fn(), new AbortController().signal))
    .rejects.toMatchObject({ message: 'The assistant returned an invalid response.', cause })
})

it.each([
  new ApiError({ message: 'Access denied', status: 403, url: '/api/ai-assistant/chat' }),
  new DOMException('Cancelled', 'AbortError'),
  new TypeError('Network unavailable'),
])('preserves other transport failures: %s', async cause => {
  vi.mocked(httpPostStream).mockRejectedValue(cause)

  await expect(streamAssistant({ messages: [{ role: 'user', text: 'Hi' }] }, vi.fn(), new AbortController().signal))
    .rejects.toBe(cause)
})
