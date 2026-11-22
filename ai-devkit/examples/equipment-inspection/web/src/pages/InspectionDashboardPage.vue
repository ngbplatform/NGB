<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  ApiError, encodeReportRouteContextParam, formatDateOnlyValue, httpGet, NgbBadge, NgbDashboardAsOfToolbar,
  NgbDashboardStatusBanner, NgbIcon, NgbPageHeader,
} from '@ngbplatform/ui'

interface Snapshot {
  asOf: string
  parameters: Record<string, string>
  total: number
  followUp: number
  rejected: number
  passed: number
}

const router = useRouter()
const snapshot = ref<Snapshot | null>(null)
const loading = ref(false)
const error = ref('')
let pending: AbortController | null = null

async function refresh(day: string | null = snapshot.value?.asOf ?? null) {
  pending?.abort()
  const request = new AbortController()
  pending = request
  loading.value = true
  error.value = ''
  try {
    const query = day ? '?' + new URLSearchParams({ asOf: day }) : ''
    const result = await httpGet<Snapshot>('/api/inspection-dashboard' + query, null, { signal: request.signal })
    if (pending === request) snapshot.value = result
  } catch (cause) {
    if (pending === request) {
      snapshot.value = null
      error.value = cause instanceof ApiError && cause.status === 403
        ? 'You do not have permission to view inspection results.'
        : 'Unable to load the inspection summary.'
    }
  } finally {
    if (pending === request) loading.value = false
  }
}

const actionCards = computed(() => snapshot.value ? [
  { title: 'Inspections flagged for follow-up', result: 'Follow-up', count: snapshot.value.followUp,
    status: 'Review', tone: 'warn' as const },
  { title: 'Rejected inspections', result: 'Rejected', count: snapshot.value.rejected,
    status: 'Requires action', tone: 'danger' as const },
] : [])

function openSummary(result?: string) {
  if (!snapshot.value) return
  const reportCode = 'example.inspection-summary'
  const request = {
    parameters: snapshot.value.parameters,
    filters: result ? { result: { value: result } } : undefined,
  }
  const ctx = encodeReportRouteContextParam({ reportCode, request })
  void router.push({ path: '/reports/' + reportCode, query: { ctx } })
}

void refresh()
onBeforeUnmount(() => {
  pending?.abort()
  pending = null
})
</script>

<template>
  <main class="flex h-full min-h-0 flex-col overflow-y-auto bg-ngb-bg">
    <NgbPageHeader title="Dashboard">
      <template #secondary>
        <p class="text-sm text-ngb-muted">
          Inspection results for the selected month
          <time v-if="snapshot" :datetime="snapshot.asOf"> · Through {{ formatDateOnlyValue(snapshot.asOf) }}</time>
        </p>
      </template>
      <template #actions>
        <NgbDashboardAsOfToolbar :model-value="snapshot?.asOf" :loading="loading"
          @update:model-value="refresh" @refresh="refresh()" />
      </template>
    </NgbPageHeader>

    <div class="grid gap-6 p-4 sm:p-6">
      <NgbDashboardStatusBanner v-if="error" :error="error" />
      <p v-if="loading" role="status">Loading inspection results…</p>
      <template v-if="snapshot && !loading">
        <section aria-label="Requires attention">
          <h2 class="mb-3 text-lg font-semibold">Requires attention</h2>
          <div class="grid gap-4 md:grid-cols-2">
            <button v-for="card in actionCards" :key="card.result" type="button"
              class="ngb-focus rounded border border-ngb-border border-l-4 bg-ngb-card p-5 text-left shadow-sm hover:bg-ngb-bg"
              :class="card.count === 0 ? 'border-l-ngb-success' : card.tone === 'danger' ? 'border-l-ngb-danger' : 'border-l-ngb-warn'"
              @click="openSummary(card.result)">
              <span class="flex items-center justify-between gap-3">
                <NgbBadge :tone="card.count ? card.tone : 'success'">{{ card.count ? card.status : 'Clear' }}</NgbBadge>
                <NgbIcon name="arrow-right" />
              </span>
              <span class="mt-4 block font-semibold">{{ card.title }}</span>
              <strong class="mt-2 block text-3xl tabular-nums">{{ card.count }}</strong>
            </button>
          </div>
        </section>

        <section aria-label="Monthly activity">
          <h2 class="mb-3 text-lg font-semibold">Monthly activity</h2>
          <div class="grid gap-4 sm:grid-cols-2">
            <button type="button" class="ngb-focus rounded border border-ngb-border bg-ngb-card p-4 text-left hover:bg-ngb-bg"
              @click="openSummary()">
              <span class="block text-sm text-ngb-muted">Completed inspections</span>
              <strong class="mt-2 block text-2xl tabular-nums">{{ snapshot.total }}</strong>
            </button>
            <button type="button" class="ngb-focus rounded border border-ngb-border bg-ngb-card p-4 text-left hover:bg-ngb-bg"
              @click="openSummary('Passed')">
              <span class="block text-sm text-ngb-muted">Marked as passed</span>
              <strong class="mt-2 block text-2xl tabular-nums">{{ snapshot.passed }}</strong>
            </button>
          </div>
        </section>
      </template>
    </div>
  </main>
</template>
