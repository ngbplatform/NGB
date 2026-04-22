<template>
  <TransitionRoot appear :show="open" as="template" @after-leave="restoreLastFocus">
    <Dialog as="div" class="relative z-40" @close="maybeClose">
      <TransitionChild
        as="template"
        enter="duration-150 ease-out"
        enter-from="opacity-0"
        enter-to="opacity-100"
        leave="duration-120 ease-in"
        leave-from="opacity-100"
        leave-to="opacity-0"
      >
      <div data-testid="drawer-overlay" class="fixed inset-0 bg-[rgba(0,0,0,.18)]" />
      </TransitionChild>

      <div class="fixed inset-0 overflow-hidden">
        <div class="absolute inset-0 overflow-hidden">
          <div
            class="pointer-events-none fixed inset-y-0 flex max-w-full"
            :class="side === 'left' ? 'left-0 pr-10' : 'right-0 pl-10'"
          >
            <TransitionChild
              as="template"
              enter="duration-150 ease-out"
              :enter-from="side === 'left' ? '-translate-x-full' : 'translate-x-full'"
              enter-to="translate-x-0"
              leave="duration-120 ease-in"
              leave-from="translate-x-0"
              :leave-to="side === 'left' ? '-translate-x-full' : 'translate-x-full'"
            >
              <DialogPanel
                data-testid="drawer-panel"
                class="pointer-events-auto w-screen bg-ngb-card shadow-card"
                :class="[side === 'left' ? 'border-r border-ngb-border' : 'border-l border-ngb-border', panelClass || 'max-w-[520px]']"
              >
                <DialogTitle v-if="hideHeader" class="sr-only">{{ title }}</DialogTitle>

                <div class="h-full flex flex-col">
                  <div v-if="!hideHeader" data-testid="drawer-header" class="px-5 py-4 border-b border-ngb-border flex items-center gap-3">
                    <div class="min-w-0 flex-1">
                      <DialogTitle class="text-base font-semibold text-ngb-text truncate">{{ title }}</DialogTitle>
                      <div v-if="subtitle" class="text-sm text-ngb-muted mt-1 truncate">{{ subtitle }}</div>
                    </div>

                    <div class="flex items-center justify-end gap-2">
                      <slot name="actions" />

                      <button v-if="showClose" class="ngb-iconbtn" @click="maybeClose" title="Close">
                        <NgbIcon name="x" />
                      </button>
                    </div>
                  </div>

                  <div data-testid="drawer-body" :class="['flex-1 overflow-auto', flushBody ? 'p-0' : 'px-5 py-4']">
                    <slot />
                  </div>

                  <div v-if="$slots.footer" class="px-5 py-4 border-t border-ngb-border">
                    <slot name="footer" />
                  </div>
                </div>
              </DialogPanel>
            </TransitionChild>
          </div>
        </div>
      </div>
    </Dialog>
  </TransitionRoot>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { Dialog, DialogPanel, DialogTitle, TransitionChild, TransitionRoot } from '@headlessui/vue';

import NgbIcon from '../primitives/NgbIcon.vue';

const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    subtitle?: string;
    showClose?: boolean;
    hideHeader?: boolean;
    flushBody?: boolean;
    side?: 'left' | 'right';
    panelClass?: string;
    // Allows parent to intercept drawer close (overlay click, Esc, or close button).
    // Return false to prevent closing.
    beforeClose?: (() => boolean | Promise<boolean>) | null;
  }>(),
  {
    showClose: true,
    hideHeader: false,
    flushBody: false,
    side: 'right',
    panelClass: '',
    beforeClose: null,
  },
);

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void;
}>();

const lastFocusedElement = ref<HTMLElement | null>(null);

function onDocumentFocusIn(event: FocusEvent) {
  if (props.open) return;
  const target = event.target instanceof HTMLElement ? event.target : null;
  if (!target || target === document.body) return;
  lastFocusedElement.value = target;
}

onMounted(() => {
  if (typeof document === 'undefined') return;
  document.addEventListener('focusin', onDocumentFocusIn, true);
});

onBeforeUnmount(() => {
  if (typeof document === 'undefined') return;
  document.removeEventListener('focusin', onDocumentFocusIn, true);
});

watch(
  () => props.open,
  (open) => {
    if (typeof window === 'undefined') return;

    if (open) {
      const activeElement = document.activeElement;
      if (activeElement instanceof HTMLElement && activeElement !== document.body) {
        lastFocusedElement.value = activeElement;
      }
    }
  },
);

function restoreLastFocus() {
  if (props.open) return;
  window.setTimeout(() => {
    if (props.open) return;
    lastFocusedElement.value?.focus?.();
  }, 0);
}

async function maybeClose() {
  if (!props.showClose) return;

  if (props.beforeClose) {
    const ok = await props.beforeClose();
    if (!ok) return;
  }

  emit('update:open', false);
}
</script>
