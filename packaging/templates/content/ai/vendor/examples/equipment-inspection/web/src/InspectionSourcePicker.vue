<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { ApiError, httpGet, NgbLookup, type LookupItem } from '@ngbplatform/ui'

const props = defineProps<{ equipmentId: string }>()
const items = ref<LookupItem[]>([])
const selected = ref<LookupItem | null>(null)
const result = ref('')
const equipmentName = ref('')
const searchError = ref('')
const previewError = ref('')
const lastQuery = ref('')
let search: AbortController | null = null
let preview: AbortController | null = null

type Source = { id: string, number: string, result: string }

async function load(query = '') {
  search?.abort()
  const request = new AbortController()
  search = request
  items.value = []
  lastQuery.value = query
  searchError.value = ''
  try {
    const parameters = new URLSearchParams({ equipmentId: props.equipmentId, query, limit: '20' })
    const sources = await httpGet<Source[]>(`/api/inspection-sources?${parameters}`, null, { signal: request.signal })
    if (search !== request) return
    items.value = sources.map(source => ({ id: source.id, label: source.number }))
  } catch {
    if (search === request) searchError.value = 'Unable to load available inspections. Retry the search.'
  }
}

async function choose(value: LookupItem | null) {
  preview?.abort()
  preview = null
  selected.value = value
  result.value = ''
  equipmentName.value = ''
  previewError.value = ''
  if (!value) return
  const request = new AbortController()
  preview = request
  try {
    const parameters = new URLSearchParams({ equipmentId: props.equipmentId })
    const source = await httpGet<Source>(`/api/inspection-sources/${value.id}?${parameters}`, null, { signal: request.signal })
    if (preview !== request) return
    const equipment = await httpGet<{ display: string }>(`/api/catalogs/example.equipment/${props.equipmentId}`, null,
      { signal: request.signal })
    // Every dependent lookup belongs to the same source selection, including the last await.
    if (preview !== request) return
    equipmentName.value = equipment.display
    result.value = source.result
  } catch (error) {
    if (preview === request) {
      if (error instanceof ApiError && error.status === 409) {
        selected.value = null
        previewError.value = error.message
      } else {
        previewError.value = 'Unable to read this inspection. Retry the preview.'
      }
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
    <div v-if="searchError">
      <p role="alert">{{ searchError }}</p>
      <button type="button" class="ngb-focus underline" @click="load(lastQuery)">Retry search</button>
    </div>
    <div v-if="previewError">
      <p role="alert">{{ previewError }}</p>
      <button v-if="selected" type="button" class="ngb-focus underline" @click="choose(selected)">Retry preview</button>
    </div>
    <p data-testid="source-equipment">Equipment: {{ equipmentName || '—' }}</p>
    <p role="status">{{ result || 'Select an available inspection to preview its result.' }}</p>
  </section>
</template>
