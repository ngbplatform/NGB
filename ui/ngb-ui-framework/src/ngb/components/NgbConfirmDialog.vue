<template>
  <NgbModalShell :open="open" max-width-class="max-w-[480px]" @close="cancel">
    <div class="p-5">
      <div class="flex items-start gap-3">
        <div class="shrink-0 mt-0.5">
          <div
            class="w-9 h-9 rounded-full flex items-center justify-center"
            :class="danger ? 'bg-[rgba(155,28,28,.08)] text-ngb-danger' : 'bg-[rgba(11,60,93,.08)] text-ngb-blue'"
          >
            <NgbIcon :name="danger ? 'trash' : 'help-circle'" />
          </div>
        </div>

        <div class="min-w-0 flex-1">
          <DialogTitle class="text-base font-semibold text-ngb-text">{{ title }}</DialogTitle>
          <div class="text-sm text-ngb-muted mt-1 leading-5">{{ message }}</div>
          <div v-if="$slots.default" class="mt-3">
            <slot />
          </div>
        </div>
      </div>

      <div class="mt-5 flex items-center justify-end gap-2">
        <NgbButton variant="secondary" @click="cancel">{{ cancelTextComputed }}</NgbButton>
        <NgbButton :variant="danger ? 'danger' : 'primary'" :loading="confirmLoading" @click="confirm">{{ confirmTextComputed }}</NgbButton>
      </div>
    </div>
  </NgbModalShell>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { DialogTitle } from '@headlessui/vue'
import NgbModalShell from './NgbModalShell.vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbIcon from '../primitives/NgbIcon.vue'

const props = defineProps<{
  open: boolean
  title: string
  message: string
  confirmText?: string
  cancelText?: string
  danger?: boolean
  confirmLoading?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'confirm'): void
}>()

const confirmTextComputed = computed(() => props.confirmText ?? (props.danger ? 'Discard' : 'Confirm'))
const cancelTextComputed = computed(() => props.cancelText ?? 'Cancel')

function cancel() {
  emit('update:open', false)
}

function confirm() {
  emit('confirm')
}
</script>
