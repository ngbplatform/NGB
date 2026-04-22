<script setup lang="ts">
import NgbConfirmDialog from '../components/NgbConfirmDialog.vue';

const props = withDefaults(defineProps<{
  open: boolean;
  title?: string;
  message?: string;
  confirmText?: string;
  cancelText?: string;
}>(), {
  title: 'Discard changes?',
  message: 'You have unsaved changes. If you close this panel now, they won’t be saved.',
  confirmText: 'Discard',
  cancelText: 'Keep editing',
});

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void;
  (e: 'confirm'): void;
  (e: 'cancel'): void;
}>();

function handleOpenUpdate(value: boolean) {
  emit('update:open', value);
  if (!value) emit('cancel');
}
</script>

<template>
  <NgbConfirmDialog
    :open="props.open"
    :title="props.title"
    :message="props.message"
    :confirm-text="props.confirmText"
    :cancel-text="props.cancelText"
    danger
    @update:open="handleOpenUpdate"
    @confirm="emit('confirm')"
  />
</template>
