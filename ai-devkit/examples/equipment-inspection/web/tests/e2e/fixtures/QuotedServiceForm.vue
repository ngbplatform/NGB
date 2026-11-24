<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getDocumentById, updateDraft } from '@ngbplatform/ui'
import QuotedLinesEditor, { type QuotedLine } from '../../../src/QuotedLinesEditor.vue'

const query = new URLSearchParams(location.search)
const draftId = query.get('draft')!
const sourceId = query.get('source')!
const type = 'example.equipment-service'
const draft = ref<Awaited<ReturnType<typeof getDocumentById>>>()
const businessDate = ref('')
const rows = ref<QuotedLine[]>([])
onMounted(async () => {
  draft.value = await getDocumentById(type, draftId)
})

async function quote(id: string, pricingDate: string, signal: AbortSignal) {
  const source = await getDocumentById(type, id, { signal })
  return { unitPriceMinor: Math.round(Number(source.payload.fields?.amount) * 100), pricingDate }
}

async function save() {
  const row = rows.value[0]
  if (!draft.value || row?.totalMinor == null) return
  await updateDraft(type, draftId, {
    fields: { ...draft.value.payload.fields, hours: row.quantity, amount: row.totalMinor / 100 },
  })
}
</script>

<template>
  <main v-if="draft">
    <label>Pricing date <input v-model="businessDate" type="date"></label>
    <QuotedLinesEditor
      v-model="rows"
      :business-date="businessDate"
      :document-key="draftId"
      :items="[{ value: sourceId, label: 'Standard service' }]"
      :quote="quote"
    />
    <button type="button" :disabled="rows[0]?.totalMinor == null" @click="save">Save estimate</button>
  </main>
</template>
