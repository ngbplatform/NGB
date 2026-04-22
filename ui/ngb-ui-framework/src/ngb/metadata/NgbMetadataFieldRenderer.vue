<script setup lang="ts">
import { computed } from 'vue'
import NgbCheckbox from '../primitives/NgbCheckbox.vue'
import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import { dataTypeKind } from './dataTypes'
import { isReferenceValue } from './entityModel'
import { resolveFieldRendererState } from './fieldRendererState'
import type { EntityFormModel, FieldMetadata, MetadataFormBehavior } from './types'
import NgbMetadataLookupControl from './NgbMetadataLookupControl.vue'

const props = defineProps<{
  field: FieldMetadata
  model: EntityFormModel
  modelValue: unknown
  entityTypeCode: string
  readonly?: boolean
  disabled?: boolean
  behavior?: MetadataFormBehavior
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: unknown): void
}>()

const rendererState = computed(() => resolveFieldRendererState({
  entityTypeCode: props.entityTypeCode,
  model: props.model,
  field: props.field,
  modelValue: props.modelValue,
  behavior: props.behavior,
}))

function update(value: unknown) {
  emit('update:modelValue', value)
}

const hasRef = computed(() => isReferenceValue(props.modelValue))
const refDisplay = computed(() => (isReferenceValue(props.modelValue) ? props.modelValue.display : null))

function normalizeSelectValue(value: unknown): unknown {
  if (dataTypeKind(props.field.dataType) !== 'Int32') return value
  if (typeof value === 'number') return Math.trunc(value)
  if (typeof value !== 'string') return value

  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : value
}

const selectOptions = computed(() =>
  (rendererState.value.fieldOptions ?? []).map((option) => ({
    ...option,
    value: normalizeSelectValue(option.value),
  })),
)

const selectValue = computed(() => normalizeSelectValue(props.modelValue))
</script>

<template>
  <div class="w-full">
    <NgbSelect
      v-if="rendererState.mode === 'select' && rendererState.fieldOptions"
      :model-value="selectValue ?? null"
      :options="selectOptions"
      :disabled="disabled || readonly"
      @update:modelValue="update"
    />

    <NgbMetadataLookupControl
      v-else-if="rendererState.mode === 'lookup' && rendererState.hint"
      :hint="rendererState.hint"
      :model-value="modelValue"
      :disabled="disabled"
      :readonly="readonly"
      :behavior="behavior"
      @update:modelValue="update"
    />

    <NgbCheckbox
      v-else-if="rendererState.mode === 'checkbox'"
      :model-value="!!modelValue"
      :disabled="disabled || readonly"
      @update:modelValue="update"
    />

    <textarea
      v-else-if="rendererState.mode === 'textarea'"
      class="min-h-[96px] w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 py-2 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus"
      :value="modelValue ?? ''"
      :readonly="readonly"
      :disabled="disabled"
      @input="update(($event.target as HTMLTextAreaElement).value)"
    />

    <NgbInput
      v-else-if="rendererState.mode === 'reference-display' && hasRef"
      type="text"
      :model-value="refDisplay ?? ''"
      :title="refDisplay ?? ''"
      :disabled="true"
      :readonly="true"
    />

    <NgbDatePicker
      v-else-if="rendererState.mode === 'date'"
      :model-value="(modelValue as string | null | undefined) ?? null"
      :disabled="disabled || readonly"
      @update:modelValue="update"
    />

    <NgbInput
      v-else
      :type="rendererState.inputType"
      :model-value="modelValue ?? ''"
      :disabled="disabled"
      :readonly="readonly"
      @update:modelValue="update"
    />

    <div v-if="field.helpText" class="mt-1 text-xs text-ngb-muted">{{ field.helpText }}</div>
  </div>
</template>
