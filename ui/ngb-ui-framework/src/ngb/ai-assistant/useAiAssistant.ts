import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'
import { useAuthStore } from '../auth/useAuthStore'
import { useFeatureStore } from '../features/useFeatureStore'
import { NGB_FEATURES } from '../features/types'
import { toErrorMessage } from '../utils/errorMessage'
import { getAssistantSettings, streamAssistant } from './api'
import type { AssistantMessage, AssistantPageContext, AssistantRequest, AssistantSettings } from './types'

const maximumVisibleMessages = 100

export const useAiAssistant = defineStore('ai-assistant', () => {
  const auth = useAuthStore()
  const features = useFeatureStore()
  const messages = ref<AssistantMessage[]>([])
  const settings = ref<AssistantSettings | null>(null)
  const busy = ref(false)
  const status = ref('')
  const error = ref('')
  const enabled = computed(() => features.isEnabled(NGB_FEATURES.aiAssistant) && features.isEnabled(NGB_FEATURES.mcp))
  let controller: AbortController | null = null
  let generation = 0
  let lastContext: AssistantPageContext | null = null

  function stop(): void {
    generation++
    controller?.abort()
    controller = null
    busy.value = false
    status.value = ''
    const last = messages.value.at(-1)
    if (last?.state === 'streaming') last.state = 'stopped'
  }

  function reset(): void {
    stop()
    messages.value = []
    error.value = ''
    lastContext = null
  }

  watch(() => [auth.subject, auth.authenticated], () => {
    reset()
    settings.value = null
  }, { flush: 'sync' })
  watch(enabled, available => {
    if (!available) {
      reset()
      settings.value = null
    }
  }, { flush: 'sync' })

  async function send(question: string, context: AssistantPageContext | null = null): Promise<void> {
    const text = question.trim()
    if (!enabled.value || busy.value || !text) return
    const current = ++generation
    const abort = new AbortController()
    controller = abort
    busy.value = true
    error.value = ''
    status.value = 'Connecting…'
    lastContext = context ? { ...context } : null
    let reply: AssistantMessage | null = null

    try {
      const limits = settings.value ?? await getAssistantSettings(abort.signal)
      if (current !== generation) return
      settings.value = limits
      if (text.length > limits.maxMessageCharacters) throw new Error(`Keep the question within ${limits.maxMessageCharacters} characters.`)
      const previous: AssistantRequest['messages'] = []
      for (let index = 0; index + 1 < messages.value.length; index += 2) {
        const user = messages.value[index]!
        const answer = messages.value[index + 1]!
        if (answer.state === 'complete' && answer.text.length <= limits.maxMessageCharacters) {
          const values = new Map(answer.values?.map(value => [value.reference, value.value]) ?? [])
          const answerText = answer.text.replace(/\{\{ngb-value:[^{}]+\}\}/g, reference => values.get(reference) ?? 'Value unavailable')
          if (answerText.length <= limits.maxMessageCharacters) {
            previous.push({ role: 'user', text: user.text }, { role: 'assistant', text: answerText })
          }
        }
      }
      const request: AssistantRequest = {
        messages: [...previous, { role: 'user', text }],
        context: lastContext,
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      }
      const encoder = new TextEncoder()
      while (request.messages.length > 1 && (request.messages.length > limits.maxMessages
        || encoder.encode(JSON.stringify(request)).length > limits.maxInputBytes)) {
        request.messages.splice(0, 2)
      }
      if (encoder.encode(JSON.stringify(request)).length > limits.maxInputBytes) throw new Error('The question is too large. Please shorten it.')

      messages.value.push({ id: crypto.randomUUID(), role: 'user', text, sources: [], state: 'complete' })
      messages.value.push({ id: crypto.randomUUID(), role: 'assistant', text: '', sources: [], values: [], state: 'streaming' })
      if (messages.value.length > maximumVisibleMessages) {
        messages.value.splice(0, messages.value.length - maximumVisibleMessages)
      }
      reply = messages.value.at(-1)!
      await streamAssistant(request, event => {
        if (current !== generation || !reply) return
        if (event.type === 'text') {
          if (reply.text.length + (event.text?.length ?? 0) > limits.maxOutputCharacters) throw new Error('The answer exceeded its size limit.')
          reply.text += event.text ?? ''
        } else if (event.type === 'status') {
          status.value = event.text ?? ''
        } else if (event.type === 'source' && event.source && reply.sources.length < 30) {
          reply.sources.push(event.source)
        } else if (event.type === 'value' && event.value && (reply.values?.length ?? 0) < 512) {
          reply.values?.push(event.value)
        } else if (event.type === 'done') {
          reply.state = 'complete'
        }
      }, abort.signal)
    } catch (cause) {
      if (current !== generation) return
      if (reply) reply.state = abort.signal.aborted ? 'stopped' : 'error'
      if (!abort.signal.aborted) error.value = toErrorMessage(cause, 'The assistant is unavailable. Please try again.')
    } finally {
      if (current === generation) {
        busy.value = false
        status.value = ''
        controller = null
      }
    }
  }

  async function retry(): Promise<void> {
    const previous = messages.value.at(-2)
    if (busy.value || previous?.role !== 'user') return
    const text = previous.text
    messages.value.splice(-2)
    await send(text, lastContext)
  }

  return { messages, settings, busy, status, error, enabled, send, stop, reset, retry }
})
