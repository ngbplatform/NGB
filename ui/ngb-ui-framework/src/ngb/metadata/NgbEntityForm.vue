<script setup lang="ts">
import { computed, ref } from 'vue'
import NgbFormLayout from '../components/forms/NgbFormLayout.vue'
import NgbFormSection from '../components/forms/NgbFormSection.vue'
import { defaultFindDisplayField } from './entityForm'
import { resolveNgbMetadataFormBehavior } from './config'
import { useValidationFocus } from './useValidationFocus'
import type { EntityFormModel, FieldMetadata, FormMetadata, MetadataFormBehavior } from './types'
import NgbEntityFormFieldsBlock from './NgbEntityFormFieldsBlock.vue'

const props = defineProps<{
  form: FormMetadata
  model: EntityFormModel
  entityTypeCode: string
  status?: number
  forceReadonly?: boolean
  presentation?: 'sections' | 'flat'
  errors?: Record<string, string | string[] | null> | null
  behavior?: MetadataFormBehavior
}>()

const rootRef = ref<HTMLElement | null>(null)
const behavior = computed(() => resolveNgbMetadataFormBehavior(props.behavior))
const displayField = computed<FieldMetadata | null>(() =>
  behavior.value.findDisplayField?.(props.form) ?? defaultFindDisplayField(props.form),
)
const presentation = computed(() => props.presentation ?? 'sections')
const sections = computed(() => props.form?.sections ?? [])
const flatRows = computed(() => sections.value.flatMap((section) => section.rows ?? []))
const validationFocus = useValidationFocus(rootRef, { attribute: 'data-validation-key' })

function scrollToField(key: string): boolean {
  return validationFocus.scrollTo(key)
}

function focusField(key: string): boolean {
  return validationFocus.focus(key)
}

function focusFirstError(keys: string[]): boolean {
  for (const key of keys) {
    if (focusField(key)) return true
  }
  return false
}

defineExpose({
  scrollToField,
  focusField,
  focusFirstError,
})
</script>

<template>
  <div ref="rootRef">
    <NgbFormLayout v-if="presentation === 'sections'">
      <NgbFormSection v-for="(section, sectionIndex) in sections" :key="sectionIndex" :title="section.title">
        <NgbEntityFormFieldsBlock
          :rows="section.rows ?? []"
          :display-field="sectionIndex === 0 ? displayField : null"
          :model="model"
          :entity-type-code="entityTypeCode"
          :status="status"
          :force-readonly="forceReadonly"
          :errors="errors"
          :behavior="behavior.value"
        />
      </NgbFormSection>
    </NgbFormLayout>

    <div v-else class="space-y-3">
      <NgbEntityFormFieldsBlock
        :rows="flatRows"
        :display-field="displayField"
        :model="model"
        :entity-type-code="entityTypeCode"
        :status="status"
        :force-readonly="forceReadonly"
        :errors="errors"
        :behavior="behavior.value"
      />
    </div>
  </div>
</template>
