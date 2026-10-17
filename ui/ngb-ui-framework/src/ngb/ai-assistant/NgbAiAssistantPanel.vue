<template>
  <NgbDrawer
      v-if="mobile"
      :open="open"
      title="AI Assistant"
      hide-header
      flush-body
      panel-class="max-w-full"
      @update:open="$emit('update:open', $event)"
      @after-leave="restoreFocus"
  >
    <NgbAiAssistantContent @close="close" @navigate="$emit('navigate', $event)" />
  </NgbDrawer>
  <aside
      v-else
      v-show="open"
      aria-labelledby="ai-assistant-title"
      class="h-full min-h-0 w-[440px] shrink-0 border-l border-ngb-border bg-ngb-card"
      data-testid="ai-assistant-panel"
      @keydown.esc.stop="close"
  >
    <NgbAiAssistantContent @close="close" @navigate="$emit('navigate', $event)" />
  </aside>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbAiAssistantContent from './NgbAiAssistantContent.vue'
import { useAiAssistant } from './useAiAssistant'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (event: 'update:open', value: boolean): void; (event: 'navigate', path: string): void }>()
const assistant = useAiAssistant()
const media = window.matchMedia('(max-width: 1023px)')
const mobile = ref(media.matches)
let previousFocus: HTMLElement | null = null

function updateViewport(): void {
  mobile.value = media.matches
}

function close(): void {
  emit('update:open', false)
  if (!mobile.value) {
    void nextTick(restoreFocus)
  }
}

function restoreFocus(): void {
  if (!props.open) {
    previousFocus?.focus()
  }
}

watch(() => props.open, async open => {
  if (open && document.activeElement instanceof HTMLElement) {
    previousFocus = document.activeElement
    await nextTick()
    document.getElementById('ai-assistant-question')?.focus()
  }
}, { immediate: true })
onMounted(() => media.addEventListener('change', updateViewport))
onBeforeUnmount(() => {
  media.removeEventListener('change', updateViewport)
  assistant.reset()
})
</script>
