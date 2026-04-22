<script setup lang="ts">
import { computed } from 'vue'
import NgbFormRow from '../components/forms/NgbFormRow.vue'
import { defaultIsFieldHidden, defaultIsFieldReadonly } from './entityForm'
import { resolveNgbMetadataFormBehavior } from './config'
import type { EntityFormModel, FieldMetadata, MetadataFormBehavior } from './types'
import NgbMetadataFieldRenderer from './NgbMetadataFieldRenderer.vue'

type FormRowDescriptor = {
  fields?: FieldMetadata[] | null
}

const props = defineProps<{
  rows: FormRowDescriptor[]
  model: EntityFormModel
  entityTypeCode: string
  status?: number
  forceReadonly?: boolean
  displayField?: FieldMetadata | null
  errors?: Record<string, string | string[] | null> | null
  behavior?: MetadataFormBehavior
}>()

const behavior = computed(() => resolveNgbMetadataFormBehavior(props.behavior))
const isDocumentEntity = computed(() => props.status !== undefined)

function isReadonlyField(field: FieldMetadata): boolean {
  const args = {
    entityTypeCode: props.entityTypeCode,
    model: props.model,
    field,
    status: props.status,
    forceReadonly: props.forceReadonly,
  }

  return behavior.value.isFieldReadonly?.(args) ?? defaultIsFieldReadonly(args)
}

function isHiddenField(field: FieldMetadata): boolean {
  const args = {
    entityTypeCode: props.entityTypeCode,
    model: props.model,
    field,
    isDocumentEntity: isDocumentEntity.value,
  }

  return behavior.value.isFieldHidden?.(args) ?? defaultIsFieldHidden(args)
}

function isDisplayField(field: FieldMetadata): boolean {
  return field.key === 'display'
}

function firstFieldError(key: string): string | undefined {
  const value = props.errors?.[key]
  if (Array.isArray(value)) return value.find((entry) => typeof entry === 'string' && entry.trim().length > 0)
  if (typeof value === 'string' && value.trim().length > 0) return value
  return undefined
}
</script>

<template>
  <div class="space-y-3">
    <div
      v-if="displayField && !isHiddenField(displayField)"
      :data-validation-key="displayField.key"
    >
      <NgbFormRow
        :label="displayField.label"
        :hint="displayField.helpText || (displayField.isRequired ? 'Required' : undefined)"
        :error="firstFieldError(displayField.key)"
      >
        <NgbMetadataFieldRenderer
          :field="displayField"
          :model="model"
          :entity-type-code="entityTypeCode"
          :model-value="model[displayField.key]"
          :readonly="isReadonlyField(displayField)"
          :behavior="behavior.value"
          @update:modelValue="model[displayField.key] = $event"
        />
      </NgbFormRow>
    </div>

    <template v-for="(row, rowIndex) in rows" :key="rowIndex">
      <template v-for="(field, fieldIndex) in row.fields ?? []" :key="`${field.key}:${fieldIndex}`">
        <div
          v-if="!isDisplayField(field) && !isHiddenField(field)"
          :data-validation-key="field.key"
        >
          <NgbFormRow
            :label="field.label"
            :hint="field.helpText || (field.isRequired ? 'Required' : undefined)"
            :error="firstFieldError(field.key)"
          >
            <NgbMetadataFieldRenderer
              :field="field"
              :model="model"
              :entity-type-code="entityTypeCode"
              :model-value="model[field.key]"
              :readonly="isReadonlyField(field)"
              :behavior="behavior.value"
              @update:modelValue="model[field.key] = $event"
            />
          </NgbFormRow>
        </div>
      </template>
    </template>
  </div>
</template>
