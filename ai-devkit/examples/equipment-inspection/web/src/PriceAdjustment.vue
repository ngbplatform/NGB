<script setup lang="ts">
import { computed } from 'vue'
import { NgbInput } from '@ngbplatform/ui'

export type Adjustment = {
  priceMinor: number
  reason: string
}
const props = defineProps<{
  modelValue: Adjustment
  quotedPriceMinor: number
  canAdjust: boolean
  readonly?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Adjustment] }>()
const editable = computed(() => props.canAdjust && !props.readonly)
const needsReason = computed(() => props.modelValue.priceMinor < props.quotedPriceMinor && !props.modelValue.reason.trim())

function changePrice(value: string | number): void {
  const priceMinor = Number(value)
  if (editable.value && String(value).trim() && Number.isSafeInteger(priceMinor) && priceMinor >= 0) {
    emit('update:modelValue', { ...props.modelValue, priceMinor })
  }
}

function changeReason(value: string | number): void {
  if (editable.value) emit('update:modelValue', { ...props.modelValue, reason: String(value) })
}
</script>

<template>
  <div class="space-y-2">
    <NgbInput
      :model-value="modelValue.priceMinor"
      :readonly="!editable"
      type="number"
      title="Agreed price (cents)"
      @update:model-value="changePrice"
    />
    <NgbInput
      :model-value="modelValue.reason"
      :readonly="!editable"
      title="Discount reason"
      @update:model-value="changeReason"
    />
    <p class="text-sm text-ngb-muted">
      A reason is required below the quoted price. You may enter it before changing the price.
    </p>
    <p v-if="!editable" class="text-sm text-ngb-muted">
      Price adjustments require an editable draft and adjustment permission.
    </p>
    <p v-if="needsReason" role="alert">Enter a discount reason before confirming.</p>
  </div>
</template>
