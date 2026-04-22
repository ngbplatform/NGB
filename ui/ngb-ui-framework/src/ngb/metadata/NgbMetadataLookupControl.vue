<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import NgbLookup from '../primitives/NgbLookup.vue'
import { resolveNgbMetadataFormBehavior } from './config'
import { isReferenceValue, tryExtractReferenceId } from './entityModel'
import type { LookupHint, LookupItem, MetadataFormBehavior } from './types'

const props = defineProps<{
  hint: LookupHint
  modelValue: unknown
  readonly?: boolean
  disabled?: boolean
  behavior?: MetadataFormBehavior
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: unknown): void
}>()

const router = useRouter()
const route = useRoute()

const behavior = computed(() => resolveNgbMetadataFormBehavior(props.behavior))
const lookupItems = ref<LookupItem[]>([])
const lookupValueId = computed(() => tryExtractReferenceId(props.modelValue))
const canOpenLookup = computed(() => !!lookupValueId.value && !!behavior.value.buildLookupTargetUrl)

const selectedItem = computed((): LookupItem | null => {
  const value = props.modelValue
  if (!value) return null

  if (isReferenceValue(value)) {
    return { id: value.id, label: value.display }
  }

  const id = tryExtractReferenceId(value)
  return id ? { id, label: id } : null
})

const canClearLookup = computed(() => !props.readonly && !props.disabled && !!selectedItem.value)

async function onLookupQuery(queryText: string) {
  const query = queryText.trim()
  if (!query || !behavior.value.searchLookup) {
    lookupItems.value = []
    return
  }

  lookupItems.value = await behavior.value.searchLookup({
    hint: props.hint,
    query,
  })
}

function onLookupSelect(value: LookupItem | null) {
  if (!value) {
    emit('update:modelValue', null)
    return
  }

  emit('update:modelValue', { id: value.id, display: value.label })
}

async function openLookupValue() {
  if (!behavior.value.buildLookupTargetUrl) return

  const target = await behavior.value.buildLookupTargetUrl({
    hint: props.hint,
    value: lookupValueId.value,
    routeFullPath: route.fullPath,
  })

  if (!target) return
  await router.push(target)
}
</script>

<template>
  <NgbLookup
    :model-value="selectedItem"
    :items="lookupItems"
    :disabled="disabled"
    :readonly="readonly"
    :show-open="canOpenLookup"
    :show-clear="canClearLookup"
    placeholder="Type to search…"
    @update:modelValue="onLookupSelect"
    @query="onLookupQuery"
    @open="void openLookupValue()"
  />
</template>
