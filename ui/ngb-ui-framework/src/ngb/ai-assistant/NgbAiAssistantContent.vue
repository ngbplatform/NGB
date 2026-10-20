<template>
  <div class="flex h-full min-h-0 flex-col" data-testid="ai-assistant-content">
    <header class="flex h-14 shrink-0 items-center gap-2 border-b border-ngb-border px-4" data-testid="ai-assistant-header">
      <div class="min-w-0 flex-1">
        <h2 id="ai-assistant-title" class="font-semibold">AI Assistant</h2>
      </div>
      <button type="button" class="ngb-iconbtn" title="New chat" aria-label="New chat" @click="newChat">
        <NgbIcon name="plus" />
      </button>
      <button type="button" class="ngb-iconbtn" title="Close assistant" aria-label="Close assistant" @click="$emit('close')">
        <NgbIcon name="x" />
      </button>
    </header>

    <div ref="conversation" class="min-h-0 min-w-0 flex-1 overflow-y-auto px-4 py-5" @scroll="trackScroll">
      <div v-if="assistant.messages.length === 0" class="space-y-3 py-6">
        <h3 class="text-lg font-semibold">How can I help?</h3>
        <p class="text-sm leading-6 text-ngb-muted">Find records, explore reports, and understand your business data.</p>
        <button
          v-for="question in suggestions"
          :key="question"
          type="button"
          class="block w-full rounded-[var(--ngb-radius)] border border-ngb-border p-3 text-left text-sm ngb-focus hover:bg-ngb-bg"
          :disabled="assistant.busy"
          @click="ask(question)"
        >{{ question }}</button>
      </div>

      <ol class="space-y-5" aria-label="Current conversation">
        <li v-for="message in assistant.messages" :key="message.id" class="min-w-0">
          <div class="mb-1 text-xs font-semibold text-ngb-muted">{{ message.role === 'user' ? 'You' : 'Assistant' }}</div>
          <div v-if="message.role === 'user'" class="whitespace-pre-wrap break-words rounded-[var(--ngb-radius)] bg-ngb-bg px-3 py-2 text-sm">{{ message.text }}</div>
          <div v-else class="assistant-markdown break-words text-sm leading-6" v-html="renderMessage(message)" @click="navigateLink" />
          <p v-if="message.state === 'stopped'" class="mt-2 text-xs text-ngb-muted">Response stopped.</p>
          <p v-if="message.state === 'error' && message.text" class="mt-2 text-xs text-ngb-muted">This answer is incomplete.</p>
        </li>
      </ol>
    </div>

    <div class="border-t border-ngb-border p-4">
      <div v-if="assistant.busy" role="status" aria-live="polite" aria-atomic="true" class="mb-2 text-ngb-muted">
        <span class="sr-only">Assistant is responding…</span>
        <span class="assistant-activity" aria-hidden="true">
          <span class="assistant-activity-dot" />
          <span class="assistant-activity-dot" />
          <span class="assistant-activity-dot" />
        </span>
      </div>
      <div v-if="assistant.error" role="alert" class="mb-3 text-sm text-ngb-danger">
        {{ assistant.error }}
        <button v-if="canRetry" type="button" class="ml-2 underline ngb-focus" @click="assistant.retry()">Retry</button>
      </div>
      <label v-if="context" class="mb-3 flex items-center gap-2 text-xs text-ngb-muted">
        <input v-model="useContext" type="checkbox" />
        Focus on {{ pageTitle || 'this section' }}
      </label>
      <form @submit.prevent="submit">
        <label for="ai-assistant-question" class="sr-only">Ask a question</label>
        <textarea
            id="ai-assistant-question"
            ref="input"
            v-model="draft"
            rows="3"
            class="w-full resize-none rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2 text-sm ngb-focus"
            placeholder="Ask about your data…"
            :maxlength="assistant.settings?.maxMessageCharacters"
            :disabled="assistant.busy"
            @keydown.enter.exact="handleEnter"
        />
        <div class="mt-2 flex items-center justify-between gap-3">
          <span class="text-xs text-ngb-muted">This chat clears when you reload.</span>
          <NgbButton v-if="assistant.busy" @click="assistant.stop()">Stop</NgbButton>
          <NgbButton v-else type="submit" variant="primary" :disabled="!draft.trim()">Send</NgbButton>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, nextTick, onMounted, ref, watch } from 'vue'
import { routeLocationKey } from 'vue-router'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbButton from '../primitives/NgbButton.vue'
import { useAiAssistant } from './useAiAssistant'
import { resolveNgbPageContext } from '../navigation/config'
import { renderAssistantMarkdown } from './markdown'
import type { AssistantMessage } from './types'

defineProps<{ pageTitle?: string }>()
const emit = defineEmits<{ (event: 'close'): void; (event: 'navigate', path: string): void }>()
const assistant = useAiAssistant()
const route = inject(routeLocationKey, null)
const context = computed(() => route ? resolveNgbPageContext(route) : null)
const useContext = ref(true)
const draft = ref('')
const conversation = ref<HTMLElement | null>(null)
const input = ref<HTMLTextAreaElement | null>(null)
const followOutput = ref(true)
const canRetry = computed(() => !assistant.busy && assistant.messages.at(-1)?.state === 'error')
const suggestions = ['What reports can I run?', 'Help me find a record.', 'What data can I explore here?']

function renderMessage(message: AssistantMessage): string {
  return renderAssistantMarkdown(message.text, message.sources, undefined, message.values)
}

function navigateLink(event: MouseEvent): void {
  const anchor = (event.target as HTMLElement).closest('a')
  if (!anchor || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return
  event.preventDefault()
  const url = new URL(anchor.href)
  if (url.origin === window.location.origin) emit('navigate', url.pathname + url.search + url.hash)
}

async function ask(question: string): Promise<void> {
  followOutput.value = true
  await assistant.send(question, useContext.value ? context.value : null)
}

async function submit(): Promise<void> {
  const question = draft.value.trim()
  if (!question || assistant.busy) return
  draft.value = ''
  await ask(question)
  if (assistant.error && assistant.messages.length === 0) draft.value = question
  await nextTick()
  input.value?.focus()
}

function handleEnter(event: KeyboardEvent): void {
  if (event.isComposing) return
  event.preventDefault()
  void submit()
}

function newChat(): void {
  assistant.reset()
  draft.value = ''
  input.value?.focus()
}

function trackScroll(): void {
  const element = conversation.value
  if (element) followOutput.value = element.scrollHeight - element.scrollTop - element.clientHeight < 64
}

watch(() => [assistant.messages.length, assistant.messages.at(-1)?.text, assistant.status], async () => {
  await nextTick()
  if (followOutput.value && conversation.value) conversation.value.scrollTop = conversation.value.scrollHeight
})
onMounted(() => input.value?.focus())
</script>

<style scoped>
.assistant-activity {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  height: 1rem;
}

.assistant-activity-dot {
  width: 0.375rem;
  height: 0.375rem;
  border-radius: 50%;
  background: currentColor;
}

@media (prefers-reduced-motion: no-preference) {
  .assistant-activity-dot {
    animation: assistant-activity 1.2s ease-in-out infinite;
  }

  .assistant-activity-dot:nth-child(2) {
    animation-delay: 0.15s;
  }

  .assistant-activity-dot:nth-child(3) {
    animation-delay: 0.3s;
  }
}

@keyframes assistant-activity {
  0%, 60%, 100% {
    opacity: 0.45;
    transform: translateY(0);
  }

  30% {
    opacity: 1;
    transform: translateY(-0.2rem);
  }
}

.assistant-markdown :deep(p + p),
.assistant-markdown :deep(ul),
.assistant-markdown :deep(ol) {
  margin-top: 0.75rem;
}

.assistant-markdown :deep(ul) {
  list-style: disc;
  padding-left: 1.25rem;
}

.assistant-markdown :deep(ol) {
  list-style: decimal;
  padding-left: 1.25rem;
}

.assistant-markdown :deep(a) {
  text-decoration: underline;
}

.assistant-markdown :deep(pre),
.assistant-markdown :deep(.assistant-table) {
  display: block;
  overflow-x: auto;
  max-width: 100%;
}

.assistant-markdown :deep(.assistant-table) {
  margin-top: 0.75rem;
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
}

.assistant-markdown :deep(table) {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8125rem;
  line-height: 1.5;
}

.assistant-markdown :deep(th),
.assistant-markdown :deep(td) {
  min-width: 7rem;
  max-width: 24rem;
  padding: 0.5rem 0.75rem;
  text-align: left;
  vertical-align: top;
  border-bottom: 1px solid var(--ngb-border);
  overflow-wrap: anywhere;
  word-break: normal;
}

.assistant-markdown :deep(.assistant-cell-value) {
  min-width: 0;
  white-space: pre-wrap;
}

.assistant-markdown :deep(th) {
  background: var(--ngb-bg);
  font-weight: 600;
}

.assistant-markdown :deep(tr:last-child td) {
  border-bottom: 0;
}
</style>
