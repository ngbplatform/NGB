<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { ApiError, httpGet, NgbLookup, type LookupItem } from '@ngbplatform/ui'

const props = defineProps<{ equipmentId: string }>()
const items = ref<LookupItem[]>([])
const selected = ref<LookupItem | null>(null)
const result = ref('')
const message = ref('')
let search: AbortController | null = null
let preview: AbortController | null = null

type Source = { id: string, number: string, result: string }

async function load(query = '') {
  search?.abort()
  const request = new AbortController()
  search = request
  items.value = []
  message.value = ''
  try {
    const parameters = new URLSearchParams({ equipmentId: props.equipmentId, query, limit: '20' })
    const sources = await httpGet<Source[]>(`/api/inspection-sources?${parameters}`, null, { signal: request.signal })
    if (search !== request) return
    items.value = sources.map(source => ({ id: source.id, label: source.number }))
  } catch {
    if (search === request) message.value = 'Unable to load available inspections. Retry the search.'
  }
}

async function choose(value: LookupItem | null) {
  preview?.abort()
  preview = null
  selected.value = value
  result.value = ''
  message.value = ''
  if (!value) return
  const request = new AbortController()
  preview = request
  try {
    const parameters = new URLSearchParams({ equipmentId: props.equipmentId })
    const source = await httpGet<Source>(`/api/inspection-sources/${value.id}?${parameters}`, null, { signal: request.signal })
    if (preview === request) result.value = source.result
  } catch (error) {
    if (preview === request) {
      selected.value = null
      message.value = error instanceof ApiError && error.status === 409
        ? error.message : 'Unable to read this inspection. Select another source.'
    }
  }
}

watch(() => props.equipmentId, () => {
  void choose(null)
  void load()
}, { immediate: true })
onBeforeUnmount(() => {
  search?.abort()
  preview?.abort()
  search = null
  preview = null
})
</script>

<template>
  <section class="space-y-4 p-6">
    <h1>Choose an inspection for follow-up</h1>
    <NgbLookup
      :model-value="selected"
      :items="items"
      label="Confirmed inspection"
      hint="Only confirmed inspections for this equipment without a confirmed follow-up are available."
      show-clear
      @query="load"
      @update:model-value="choose"
    />
    <p v-if="message" role="alert">{{ message }}</p>
    <p role="status">{{ result || 'Select an available inspection to preview its result.' }}</p>
  </section>
</template>
