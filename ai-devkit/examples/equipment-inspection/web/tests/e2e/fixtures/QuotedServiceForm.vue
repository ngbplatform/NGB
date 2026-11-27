<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getDocumentById, getDocumentPage, updateDraft } from '@ngbplatform/ui'
import QuotedLinesEditor, { type QuotedLine } from '../../../src/QuotedLinesEditor.vue'
import PriceAdjustment, { type Adjustment } from '../../../src/PriceAdjustment.vue'

const query = new URLSearchParams(location.search)
const draftId = query.get('draft')!
const sourceId = query.get('source')!
const priceList = query.get('priceList')
const type = 'example.equipment-service'
const draft = ref<Awaited<ReturnType<typeof getDocumentById>>>()
const businessDate = ref('')
const rows = ref<QuotedLine[]>([])
const manual = ref<Adjustment | null>(null)
const agreed = computed(() => manual.value ?? { priceMinor: rows.value[0]?.totalMinor ?? 0, reason: '' })

onMounted(async () => {
  draft.value = await getDocumentById(type, draftId)
})

async function quote(id: string, pricingDate: string, signal: AbortSignal) {
  if (priceList) {
    const page = await getDocumentPage(type, { search: priceList + ' ' + pricingDate, limit: 12 }, { signal })
    if (page.items.length !== 1) throw new Error('Expected one effective price for this item.')
    return { unitPriceMinor: Math.round(Number(page.items[0].payload.fields?.amount) * 100), pricingDate }
  }
  const source = await getDocumentById(type, id, { signal })
  return { unitPriceMinor: Math.round(Number(source.payload.fields?.amount) * 100), pricingDate }
}

async function save() {
  const row = rows.value[0]
  if (!draft.value || row?.totalMinor == null) return
  await updateDraft(type, draftId, {
    fields: {
      ...draft.value.payload.fields,
      hours: row.quantity,
      amount: agreed.value.priceMinor / 100,
      quoted_amount: row.totalMinor / 100,
      adjustment_reason: agreed.value.reason || null,
      pricing_date: row.pricingDate,
    },
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
    <PriceAdjustment
      v-if="rows[0]?.totalMinor != null"
      :model-value="agreed"
      :quoted-price-minor="rows[0].totalMinor"
      :can-adjust="true"
      @update:model-value="value => manual = value"
    />
    <button type="button" :disabled="rows[0]?.totalMinor == null" @click="save">Save estimate</button>
  </main>
</template>
