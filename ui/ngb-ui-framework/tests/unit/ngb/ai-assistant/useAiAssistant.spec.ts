import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { beforeEach, expect, it, vi } from 'vitest'
import { getAssistantSettings, streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import { useAiAssistant } from '../../../../src/ngb/ai-assistant/useAiAssistant'
import { useFeatureStore } from '../../../../src/ngb/features/useFeatureStore'
import type { AssistantEvent } from '../../../../src/ngb/ai-assistant/types'

const identity = reactive({ subject: 'alex', authenticated: true })
vi.mock('../../../../src/ngb/auth/useAuthStore', () => ({ useAuthStore: () => identity }))
vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))
vi.mock('../../../../src/ngb/ai-assistant/api', () => ({ getAssistantSettings: vi.fn(), streamAssistant: vi.fn() }))

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

it('sends bounded conversational context and retains only text from previous turns', async () => {
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
