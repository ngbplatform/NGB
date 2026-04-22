<template>
  <NgbModalShell :open="open" max-width-class="max-w-[560px]" @close="$emit('update:open', false)">
    <div class="px-5 py-4 border-b border-ngb-border">
      <DialogTitle class="text-base font-semibold text-ngb-text">{{ title }}</DialogTitle>
      <div v-if="subtitle" class="text-sm text-ngb-muted mt-1">{{ subtitle }}</div>
    </div>

    <div class="px-5 py-4">
      <slot />
    </div>

    <div class="px-5 py-4 border-t border-ngb-border flex items-center justify-end gap-2">
      <slot name="footer">
        <NgbButton variant="secondary" @click="$emit('update:open', false)">{{ cancelText }}</NgbButton>
        <NgbButton :variant="danger ? 'danger' : 'primary'" :loading="confirmLoading" @click="$emit('confirm')">{{ confirmText }}</NgbButton>
      </slot>
    </div>
  </NgbModalShell>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { DialogTitle } from '@headlessui/vue'
import NgbModalShell from './NgbModalShell.vue'
import NgbButton from '../primitives/NgbButton.vue'

const props = defineProps<{
  open: boolean
  title: string
  subtitle?: string
  confirmText?: string
  cancelText?: string
  danger?: boolean
  confirmLoading?: boolean
}>()

defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'confirm'): void
}>()

const confirmText = computed(() => props.confirmText ?? 'Confirm')
const cancelText = computed(() => props.cancelText ?? 'Cancel')
</script>
