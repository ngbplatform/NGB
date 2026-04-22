<template>
  <TransitionRoot appear :show="open" as="template" @after-leave="restoreLastFocus">
    <Dialog as="div" class="relative z-50" @close="emit('close')">
      <TransitionChild
        as="template"
        enter="duration-150 ease-out"
        enter-from="opacity-0"
        enter-to="opacity-100"
        leave="duration-120 ease-in"
        leave-from="opacity-100"
        leave-to="opacity-0"
      >
        <div class="fixed inset-0 bg-[rgba(0,0,0,.28)]" />
      </TransitionChild>

      <div class="fixed inset-0 overflow-y-auto">
        <div class="min-h-full flex items-center justify-center p-4">
          <TransitionChild
            as="template"
            enter="duration-150 ease-out"
            enter-from="opacity-0 translate-y-2 scale-[0.98]"
            enter-to="opacity-100 translate-y-0 scale-100"
            leave="duration-120 ease-in"
            leave-from="opacity-100 translate-y-0 scale-100"
            leave-to="opacity-0 translate-y-2 scale-[0.98]"
          >
            <DialogPanel
              class="w-full rounded-[var(--ngb-radius)] bg-ngb-card border border-ngb-border shadow-card overflow-hidden"
              :class="maxWidthClass"
            >
              <slot />
            </DialogPanel>
          </TransitionChild>
        </div>
      </div>
    </Dialog>
  </TransitionRoot>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Dialog, DialogPanel, TransitionChild, TransitionRoot } from '@headlessui/vue'

const props = withDefaults(defineProps<{
  open: boolean
  maxWidthClass?: string
}>(), {
  maxWidthClass: 'max-w-[560px]',
})

const emit = defineEmits<{
  (e: 'close'): void
}>()

const lastFocusedElement = ref<HTMLElement | null>(null)

function onDocumentFocusIn(event: FocusEvent) {
  if (props.open) return
  const target = event.target instanceof HTMLElement ? event.target : null
  if (!target || target === document.body) return
  lastFocusedElement.value = target
}

onMounted(() => {
  if (typeof document === 'undefined') return
  document.addEventListener('focusin', onDocumentFocusIn, true)
})

onBeforeUnmount(() => {
  if (typeof document === 'undefined') return
  document.removeEventListener('focusin', onDocumentFocusIn, true)
})

watch(
  () => props.open,
  (open) => {
    if (typeof window === 'undefined') return

    if (open) {
      const activeElement = document.activeElement
      if (activeElement instanceof HTMLElement && activeElement !== document.body) {
        lastFocusedElement.value = activeElement
      }
    }
  },
)

function restoreLastFocus(attempt = 0) {
  if (props.open) return
  const restoreTarget = lastFocusedElement.value
  window.setTimeout(() => {
    if (props.open) return
    restoreTarget?.focus?.()
    if (document.activeElement === restoreTarget) return
    if (attempt >= 4) return
    restoreLastFocus(attempt + 1)
  }, attempt === 0 ? 0 : 16)
}
</script>
