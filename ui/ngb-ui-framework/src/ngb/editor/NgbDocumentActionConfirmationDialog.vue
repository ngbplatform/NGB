<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import NgbConfirmDialog from '../components/NgbConfirmDialog.vue'
import type { DocumentActionConfirmationState } from './useConfiguredEntityEditorDocumentActions'

const props = defineProps<{
  confirmation: DocumentActionConfirmationState | null
}>()

const emit = defineEmits<{
  (event: 'cancel'): void
  (event: 'confirm', reason: string | null): void
}>()

const reason = ref('')
const normalizedReason = computed(() => reason.value.trim())

watch(
  () => props.confirmation?.actionCode ?? null,
  () => { reason.value = '' },
)

function confirm(): void {
  if (props.confirmation?.requireReason && !normalizedReason.value) return
  emit('confirm', normalizedReason.value || null)
}
</script>

<template>
  <NgbConfirmDialog
    :open="confirmation !== null"
    :title="confirmation?.title ?? ''"
    :message="confirmation?.message ?? ''"
    :confirm-text="confirmation?.confirmLabel ?? 'Confirm'"
    cancel-text="Cancel"
    :danger="confirmation?.danger ?? false"
    :confirm-loading="confirmation?.loading ?? false"
    :confirm-disabled="confirmation?.requireReason === true && normalizedReason.length === 0"
    @update:open="(open) => { if (!open) emit('cancel') }"
    @confirm="confirm"
  >
    <label v-if="confirmation?.requireReason" class="block">
      <span class="mb-1.5 block text-sm font-medium text-ngb-text">Reason</span>
      <textarea
        v-model="reason"
        rows="3"
        class="w-full resize-y rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-surface px-3 py-2 text-sm text-ngb-text outline-none focus:border-ngb-blue focus:ring-2 focus:ring-ngb-blue/20"
        autocomplete="off"
        aria-label="Reason"
      />
    </label>
  </NgbConfirmDialog>
</template>
