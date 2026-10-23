import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { getAssistantSettings, streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import { useAiAssistant } from '../../../../src/ngb/ai-assistant/useAiAssistant'
import { useFeatureStore } from '../../../../src/ngb/features/useFeatureStore'
import type { AssistantEvent, AssistantMessage, AssistantSettings } from '../../../../src/ngb/ai-assistant/types'

const identity = reactive({ subject: 'alex', authenticated: true })
vi.mock('../../../../src/ngb/auth/useAuthStore', () => ({ useAuthStore: () => identity }))
vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))
vi.mock('../../../../src/ngb/ai-assistant/api', () => ({ getAssistantSettings: vi.fn(), streamAssistant: vi.fn() }))

afterEach(() => vi.restoreAllMocks())

beforeEach(() => {
  vi.resetAllMocks()
  setActivePinia(createPinia())
  identity.subject = 'alex'
  identity.authenticated = true
  useFeatureStore().current = ['Mcp', 'AiAssistant'].map(code => ({ code, enabled: true, displayName: code, group: 'Integrations' }))
  vi.mocked(getAssistantSettings).mockResolvedValue({
    maxMessages: 3, maxMessageCharacters: 8000, maxInputBytes: 32768, maxOutputCharacters: 16384, provider: 'Ollama', model: 'local',
  })
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'text', text: 'Seven records.' })
    event({ type: 'done' })
  })
})

it('sends bounded conversational history', async () => {
  const assistant = useAiAssistant()
  await assistant.send('First')
  await assistant.send('Second')
  await assistant.send('Third', { kind: 'report', typeCode: 'pm.aging' })

  expect(vi.mocked(streamAssistant).mock.calls[2]![0]).toEqual({
    messages: [{ role: 'user', text: 'Second' }, { role: 'assistant', text: 'Seven records.' }, { role: 'user', text: 'Third' }],
    context: { kind: 'report', typeCode: 'pm.aging' },
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
  })
  expect(assistant.messages).toHaveLength(6)
  expect(assistant.busy).toBe(false)
})

it('keeps historical page identities and navigation candidates distinct from the current page', async () => {
  const first = { kind: 'document' as const, typeCode: 'sample.invoice', id: '11111111-1111-4111-8111-111111111111' }
  const second = { ...first, id: '22222222-2222-4222-8222-222222222222' }
  const source = { ...first, title: 'Invoice 1' }
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    event({ type: 'source', source })
    event({ type: 'text', text: 'Invoice 1 found' })
    event({ type: 'done' })
  })
  const assistant = useAiAssistant()
  await assistant.send('Show this invoice', first)
  await assistant.send('Now show this one', second)

  const request = vi.mocked(streamAssistant).mock.calls[1]![0]
  expect(request.context).toEqual(second)
  expect(request.messages[0]?.context).toEqual(first)
  expect(request.messages[1]?.sources).toEqual([source])
})

it('stops generation and ignores late chunks from the previous request', async () => {
  let publish!: (event: AssistantEvent) => void
  let finish!: () => void
  vi.mocked(streamAssistant).mockImplementation(async (_, callback) => {
    publish = callback
    await new Promise<void>(resolve => { finish = resolve })
  })
  const assistant = useAiAssistant()
  const pending = assistant.send('Question')
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  publish({ type: 'text', text: 'Partial' })

  assistant.stop()
  publish({ type: 'text', text: 'Late' })
  finish()
  await pending

  expect(vi.mocked(streamAssistant).mock.calls[0]![2].aborted).toBe(true)
  expect(assistant.messages.at(-1)).toMatchObject({ text: 'Partial', state: 'stopped' })
})

it('keeps display references local to a reply and sends original values in bounded follow-up history', async () => {
  const reference = '{{ngb-value:abcdef012345-0}}'
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    event({ type: 'value', value: { reference, kind: 'timestamp', value: '2026-05-10T20:43:34Z' } })
    event({ type: 'text', text: `Created: ${reference}` })
    event({ type: 'done' })
  })
  const assistant = useAiAssistant()
  await assistant.send('When was it created?')
  await assistant.send('Who created it?')

  expect(assistant.messages[1]?.values).toHaveLength(1)
  expect(assistant.messages[3]?.values).toEqual([])
  expect(vi.mocked(streamAssistant).mock.calls[1]![0].messages[1]?.text).toBe('Created: 2026-05-10T20:43:34Z')
})

it.each(['subject', 'authenticated', 'feature'] as const)('clears private conversation on %s changes', async change => {
  const assistant = useAiAssistant()
  await assistant.send('Private question')

  if (change === 'subject') identity.subject = 'sam'
  if (change === 'authenticated') identity.authenticated = false
  if (change === 'feature') useFeatureStore().current = []

  expect(assistant.messages).toEqual([])
  expect(assistant.settings).toBeNull()
})

it('requires both deployment flags', async () => {
  useFeatureStore().current = [{ code: 'AiAssistant', enabled: true, displayName: 'AI Assistant', group: 'Integrations' }]
  const assistant = useAiAssistant()
  await assistant.send('Question')

  expect(assistant.enabled).toBe(false)
  expect(streamAssistant).not.toHaveBeenCalled()

  useFeatureStore().current.push({ code: 'Mcp', enabled: true, displayName: 'MCP', group: 'Integrations' })
  await assistant.send('Question')

  expect(assistant.enabled).toBe(true)
  expect(assistant.messages.at(-1)?.state).toBe('complete')
})

it('retries a failed turn without replaying partial text or duplicating the question', async () => {
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    event({ type: 'text', text: 'Incomplete' })
    throw new Error('Unavailable')
  })
  const assistant = useAiAssistant()
  await assistant.send('Question')
  expect(assistant.messages.at(-1)?.state).toBe('error')

  await assistant.retry()

  expect(assistant.messages).toHaveLength(2)
  expect(assistant.messages.at(-1)?.state).toBe('complete')
  expect(vi.mocked(streamAssistant).mock.calls[1]![0].messages).toEqual([{ role: 'user', text: 'Question' }])
})

it('does not start a stream if reset occurs while settings are loading', async () => {
  let resolveSettings!: (settings: AssistantSettings) => void
  vi.mocked(getAssistantSettings).mockReturnValueOnce(new Promise(resolve => { resolveSettings = resolve }))
  const assistant = useAiAssistant()
  const pending = assistant.send('Question')
  assistant.reset()
  resolveSettings({
    maxMessages: 3, maxMessageCharacters: 8000, maxInputBytes: 32768, maxOutputCharacters: 16384,
    provider: 'Ollama', model: 'local',
  })
  await pending

  expect(streamAssistant).not.toHaveBeenCalled()
  expect(assistant.settings).toBeNull()
  expect(assistant.messages).toEqual([])
  expect(assistant.busy).toBe(false)
})

it.each([
  [{ maxMessageCharacters: 4 }, 'Question', 'within 4 characters'],
  [{ maxInputBytes: 100 }, '😀'.repeat(100), 'question is too large'],
])('rejects oversized questions before adding a conversation turn', async (limits, question, error) => {
  const assistant = useAiAssistant()
  assistant.settings = { ...await getAssistantSettings(), ...limits }

  await assistant.send(question)

  expect(streamAssistant).not.toHaveBeenCalled()
  expect(assistant.error).toContain(error)
  expect(assistant.messages).toEqual([])
  expect(assistant.busy).toBe(false)
})

it('keeps only complete history that fits after expanding display values', async () => {
  const assistant = useAiAssistant()
  assistant.settings = { ...await getAssistantSettings(), maxMessages: 20, maxMessageCharacters: 100 }
  const reference = '{{ngb-value:abcdef012345-0}}'
  const answers: Partial<AssistantMessage>[] = [
    { text: 'x'.repeat(101) },
    { text: reference, values: [{ reference, kind: 'text', value: 'x'.repeat(101) }] },
    { text: reference },
    { text: 'Incomplete', state: 'error' },
  ]
  assistant.messages = answers.flatMap((answer, index) => [
    { id: `user-${index}`, role: 'user' as const, text: `Question ${index}`, sources: [], state: 'complete' as const },
    { id: `answer-${index}`, role: 'assistant' as const, sources: [], state: 'complete' as const, text: '', ...answer },
  ])

  await assistant.send('Next')

  expect(vi.mocked(streamAssistant).mock.calls[0]![0].messages).toEqual([
    { role: 'user', text: 'Question 2' },
    { role: 'assistant', text: 'Value unavailable' },
    { role: 'user', text: 'Next' },
  ])
})

it('bounds the displayed conversation by evicting complete oldest turns', async () => {
  const assistant = useAiAssistant()
  for (let index = 0; index < 51; index++) {
    await assistant.send(`Question ${index}`)
  }

  expect(assistant.messages).toHaveLength(100)
  expect(assistant.messages[0]).toMatchObject({ role: 'user', text: 'Question 1' })
  expect(assistant.messages.at(-2)?.text).toBe('Question 50')
  expect(assistant.messages.at(-1)?.state).toBe('complete')
})

it('fails an oversized streamed answer without appending the excessive chunk', async () => {
  const assistant = useAiAssistant()
  assistant.settings = { ...await getAssistantSettings(), maxOutputCharacters: 5 }
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    event({ type: 'text', text: 'First' })
    event({ type: 'text', text: 'too much' })
  })

  await assistant.send('Question')

  expect(assistant.messages.at(-1)).toMatchObject({ text: 'First', state: 'error' })
  expect(assistant.error).toBe('The answer exceeded its size limit.')
  expect(assistant.busy).toBe(false)
})

it('handles status and heartbeat events without adding them to the answer', async () => {
  const assistant = useAiAssistant()
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    event({ type: 'status', text: 'Reading records' })
    expect(assistant.status).toBe('Reading records')
    event({ type: 'heartbeat' })
    event({ type: 'text' })
    expect(assistant.messages.at(-1)?.text).toBe('')
    event({ type: 'status' })
    expect(assistant.status).toBe('')
    event({ type: 'done' })
  })

  await assistant.send('Question')
  expect(assistant.messages.at(-1)?.state).toBe('complete')
})

it('tolerates a reply without a display-value collection', async () => {
  const assistant = useAiAssistant()
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    delete assistant.messages.at(-1)!.values
    event({ type: 'value', value: { reference: '{{ngb-value:abcdef012345-0}}', kind: 'text', value: 'Value' } })
    event({ type: 'done' })
  })

  await assistant.send('Question')
  expect(assistant.error).toBe('')
  expect(assistant.messages.at(-1)?.state).toBe('complete')
})

it('ignores a late failure from a stopped request while a new reply succeeds', async () => {
  let rejectStream!: (reason: Error) => void
  vi.mocked(streamAssistant).mockImplementationOnce(() => new Promise((_, reject) => { rejectStream = reject }))
  const assistant = useAiAssistant()
  const pending = assistant.send('Old question')
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  assistant.stop()
  await assistant.send('New question')
  rejectStream(new Error('Old request failed'))
  await pending

  expect(assistant.error).toBe('')
  expect(assistant.messages.at(-1)).toMatchObject({ text: 'Seven records.', state: 'complete' })
})

it('treats an aborted transport as stopped without surfacing an error', async () => {
  const controller = new AbortController()
  vi.spyOn(globalThis, 'AbortController').mockImplementation(function () { return controller })
  vi.mocked(streamAssistant).mockImplementationOnce(async () => {
    controller.abort()
    throw new DOMException('Cancelled', 'AbortError')
  })
  const assistant = useAiAssistant()

  await assistant.send('Question')

  expect(assistant.messages.at(-1)?.state).toBe('stopped')
  expect(assistant.error).toBe('')
  expect(assistant.busy).toBe(false)
})

it('ignores retry when no user turn exists or a request is already running', async () => {
  const assistant = useAiAssistant()
  await assistant.retry()
  expect(streamAssistant).not.toHaveBeenCalled()

  let finish!: () => void
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, event) => {
    await new Promise<void>(resolve => { finish = resolve })
    event({ type: 'done' })
  })
  const pending = assistant.send('Question')
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  await assistant.retry()
  await assistant.send('Duplicate')
  expect(assistant.messages).toHaveLength(2)
  expect(streamAssistant).toHaveBeenCalledOnce()
  finish()
  await pending
})
