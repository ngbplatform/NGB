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
    <NgbAiAssistantContent :page-title="pageTitle" @close="close" @navigate="$emit('navigate', $event)" />
  </NgbDrawer>
  <aside
      v-else
      v-show="open"
      ref="panel"
      id="ai-assistant-panel"
      aria-labelledby="ai-assistant-title"
      class="relative h-full min-h-0 shrink-0 border-l border-ngb-border bg-ngb-card"
      :style="{ width: `${width}px` }"
      data-testid="ai-assistant-panel"
      @keydown.esc.stop="close"
  >
    <div
        role="separator"
        aria-label="Resize AI Assistant"
        aria-orientation="vertical"
        aria-controls="ai-assistant-panel"
        :aria-valuemin="minimumWidth"
        :aria-valuemax="maximumWidth"
        :aria-valuenow="width"
        tabindex="0"
        class="absolute inset-y-0 -left-1 z-10 w-2 cursor-col-resize touch-none ngb-focus"
        @pointerdown="startResize"
        @pointermove="resize"
        @pointerup="stopResize"
        @pointercancel="stopResize"
        @lostpointercapture="stopResize"
        @keydown="resizeWithKeyboard"
    />
    <NgbAiAssistantContent :page-title="pageTitle" @close="close" @navigate="$emit('navigate', $event)" />
  </aside>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbAiAssistantContent from './NgbAiAssistantContent.vue'
import { useAiAssistant } from './useAiAssistant'

const props = defineProps<{ open: boolean; pageTitle?: string }>()
const emit = defineEmits<{ (event: 'update:open', value: boolean): void; (event: 'navigate', path: string): void }>()
const assistant = useAiAssistant()
const media = window.matchMedia('(max-width: 1023px)')
const mobile = ref(media.matches)
const minimumWidth = 320
const maximumWidth = ref(availableWidth())
const width = ref(Math.min(440, maximumWidth.value))
const panel = ref<HTMLElement | null>(null)
const userSelectProperty = CSS.supports('user-select', 'none') ? 'user-select' : '-webkit-user-select'
let previousFocus: HTMLElement | null = null
let drag: {
  handle: HTMLElement
  pointerId: number
  startX: number
  startWidth: number
  cursor: string
  userSelect: string
  userSelectPriority: string
} | null = null

function availableWidth(): number {
  return Math.max(minimumWidth, Math.min(960, window.innerWidth - 640))
}

function setWidth(value: number): void {
  width.value = Math.round(Math.max(minimumWidth, Math.min(maximumWidth.value, value)))
}

function updateViewport(): void {
  mobile.value = media.matches
  maximumWidth.value = availableWidth()
  setWidth(width.value)
  stopResize()
}

function startResize(event: PointerEvent): void {
  if (event.button !== 0 || !event.isPrimary) return
  event.preventDefault()
  const handle = event.currentTarget as HTMLElement
  handle.focus()
  handle.setPointerCapture(event.pointerId)
  drag = {
    handle,
    pointerId: event.pointerId,
    startX: event.clientX,
    startWidth: panel.value?.getBoundingClientRect().width ?? width.value,
    cursor: document.body.style.cursor,
    userSelect: document.body.style.getPropertyValue(userSelectProperty),
    userSelectPriority: document.body.style.getPropertyPriority(userSelectProperty),
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.setProperty(userSelectProperty, 'none')
}

function resize(event: PointerEvent): void {
  if (drag && event.pointerId === drag.pointerId) {
    setWidth(drag.startWidth + drag.startX - event.clientX)
  }
}

function stopResize(): void {
  if (!drag) return
  const previous = drag
  drag = null
  document.body.style.cursor = previous.cursor
  document.body.style.setProperty(userSelectProperty, previous.userSelect, previous.userSelectPriority)
  if (previous.handle.hasPointerCapture(previous.pointerId)) {
    previous.handle.releasePointerCapture(previous.pointerId)
  }
}

function resizeWithKeyboard(event: KeyboardEvent): void {
  const step = event.shiftKey ? 50 : 10
  const next = {
    ArrowLeft: width.value + step,
    ArrowRight: width.value - step,
    Home: minimumWidth,
    End: maximumWidth.value,
  }[event.key]
  if (next === undefined) return
  event.preventDefault()
  setWidth(next)
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
  if (!open) stopResize()
  if (open && document.activeElement instanceof HTMLElement) {
    previousFocus = document.activeElement
    await nextTick()
    document.getElementById('ai-assistant-question')?.focus()
  }
}, { immediate: true })
onMounted(() => {
  media.addEventListener('change', updateViewport)
  window.addEventListener('resize', updateViewport)
})
onBeforeUnmount(() => {
  stopResize()
  media.removeEventListener('change', updateViewport)
  window.removeEventListener('resize', updateViewport)
  assistant.reset()
})
</script>
