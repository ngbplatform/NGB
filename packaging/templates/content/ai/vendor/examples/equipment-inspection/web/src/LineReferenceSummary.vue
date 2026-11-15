<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'

const props = defineProps<{
  rows: { item: string, quantity: number }[]
  documentKey: string
  loadItem: (id: string, signal: AbortSignal) => Promise<{ preparationMinutes: number }>
}>()
const details = shallowRef(new Map<string, number>())
const message = ref('')
let pending: AbortController | null = null
const itemIds = computed(() => [...new Set(props.rows.map(row => row.item).filter(Boolean))].sort())
const total = computed(() => {
  let minutes = 0
  for (const row of props.rows) {
    const value = details.value.get(row.item)
    if (value === undefined) return null
    minutes += value * row.quantity
  }
  return Number.isSafeInteger(minutes) ? minutes : null
})

async function hydrate(): Promise<void> {
  pending?.abort()
  const request = new AbortController()
  pending = request
  details.value = new Map()
  message.value = ''
  try {
    const values = await Promise.all(itemIds.value.map(async id => {
      const item = await props.loadItem(id, request.signal)
      if (!Number.isSafeInteger(item.preparationMinutes) || item.preparationMinutes < 0) {
        throw new Error('Invalid preparation duration.')
      }
      return [id, item.preparationMinutes] as const
    }))
    if (pending === request) details.value = new Map(values)
  } catch {
    if (pending === request) message.value = 'Preparation details are unavailable. Retry before continuing.'
  }
}

// Hydrate every persisted row, not just rows added in this browser session.
// These reference details never replace stored price/date snapshots in the document.
watch([() => props.documentKey, () => JSON.stringify(itemIds.value)], hydrate, { immediate: true })
onBeforeUnmount(() => {
  pending?.abort()
  pending = null
})
</script>

<template>
  <div>
    <p role="status">Preparation (minutes): {{ total ?? '—' }}</p>
    <p v-if="message" role="alert">
      {{ message }}
      <button type="button" class="ngb-focus underline" @click="hydrate">Retry details</button>
    </p>
  </div>
</template>
