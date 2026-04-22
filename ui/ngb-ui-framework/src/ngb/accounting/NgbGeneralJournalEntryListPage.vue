<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbRegisterGrid from '../components/register/NgbRegisterGrid.vue'
import NgbEntityListPageHeader from '../metadata/NgbEntityListPageHeader.vue'
import NgbDocumentPeriodFilter from '../metadata/NgbDocumentPeriodFilter.vue'
import NgbRecycleBinFilter from '../metadata/NgbRecycleBinFilter.vue'
import {
  monthValueToDateOnlyEnd,
  monthValueToDateOnlyStart,
  useMonthPagedListQuery,
} from '../metadata/monthPagedListQuery'
import { navigateBack } from '../router/backNavigation'
import { normalizeDocumentStatusValue } from '../editor/documentStatus'
import { toErrorMessage } from '../utils/errorMessage'
import {
  generalJournalEntryApprovalStateLabel,
  generalJournalEntryJournalTypeLabel,
  generalJournalEntrySourceLabel,
} from './generalJournalEntry'
import { getGeneralJournalEntryPage } from './generalJournalEntryApi'
import { buildGeneralJournalEntriesPath } from './navigation'
import type { GeneralJournalEntryPageDto } from './generalJournalEntryTypes'

const props = withDefaults(defineProps<{
  title?: string
  backTarget?: string | null
  storageKey?: string | null
}>(), {
  title: 'Journal Entries',
  backTarget: '/',
  storageKey: null,
})

const route = useRoute()
const router = useRouter()

const loading = ref(false)
const error = ref<string | null>(null)
const page = ref<GeneralJournalEntryPageDto | null>(null)
let loadSequence = 0

const {
  offset,
  limit,
  trashMode,
  periodFromMonth,
  periodToMonth,
  nextPage,
  prevPage,
} = useMonthPagedListQuery({
  route,
  router,
})

const resolvedBackTarget = computed(() => String(props.backTarget ?? '').trim() || '/')
const resolvedStorageKey = computed(() => String(props.storageKey ?? '').trim() || `ngb:accounting:gje:list:${route.path}`)

function fmtDate(value: string | null | undefined): string {
  const s = String(value ?? '').trim()
  if (!s) return '—'
  const d = new Date(s)
  if (Number.isNaN(d.getTime())) return s
  return d.toLocaleDateString()
}

async function load() {
  const seq = ++loadSequence
  loading.value = true
  error.value = null
  try {
    const nextPage = await getGeneralJournalEntryPage({
      offset: offset.value,
      limit: limit.value,
      dateFrom: monthValueToDateOnlyStart(periodFromMonth.value) ?? null,
      dateTo: monthValueToDateOnlyEnd(periodToMonth.value) ?? null,
      trash: trashMode.value,
    })
    if (seq !== loadSequence) return
    page.value = nextPage
  } catch (cause) {
    if (seq !== loadSequence) return
    error.value = toErrorMessage(cause, 'Failed to load journal entries.')
  } finally {
    if (seq === loadSequence) loading.value = false
  }
}

watch(
  () => [route.fullPath],
  () => {
    void load()
  },
  { immediate: true },
)

const columns = [
  { key: 'display', title: 'Display', width: 320, pinned: 'left' as const },
  { key: 'dateUtc', title: 'Date', width: 120, format: (value: unknown) => fmtDate(value as string | null | undefined) },
  { key: 'journalType', title: 'Journal Type', width: 140, format: (value: unknown) => generalJournalEntryJournalTypeLabel(value) },
  { key: 'approvalState', title: 'Approval', width: 130, format: (value: unknown) => generalJournalEntryApprovalStateLabel(value) },
  { key: 'source', title: 'Source', width: 100, format: (value: unknown) => generalJournalEntrySourceLabel(value) },
  { key: 'memo', title: 'Memo', width: 260 },
]

const rows = computed(() => {
  return (page.value?.items ?? []).map((item) => {
    const status = normalizeDocumentStatusValue(item.documentStatus)
    const isMarkedForDeletion = item.isMarkedForDeletion || status === 3

    return {
      key: item.id,
      __status: status === 2 ? 'posted' : isMarkedForDeletion ? 'marked' : 'saved',
      status,
      isMarkedForDeletion,
      display: item.display ?? item.number ?? item.id,
      dateUtc: item.dateUtc,
      journalType: item.journalType,
      approvalState: item.approvalState,
      source: item.source,
      memo: item.memo ?? '—',
    }
  })
})

function openCreate() {
  void router.push(buildGeneralJournalEntriesPath(null, { basePath: route.path }))
}

function openEdit(id: string) {
  void router.push(buildGeneralJournalEntriesPath(id, { basePath: route.path }))
}
</script>

<template>
  <div data-testid="journal-entry-list-page" class="flex h-full min-h-0 flex-col">
    <NgbEntityListPageHeader
      :title="title"
      :can-back="route.path !== resolvedBackTarget"
      :items-count="page ? page.items.length : null"
      :total="page?.total ?? null"
      :loading="loading"
      :show-filter="false"
      :disable-prev="offset === 0"
      :disable-next="(page?.items?.length ?? 0) < limit"
      @back="navigateBack(router, route, resolvedBackTarget)"
      @refresh="load"
      @create="openCreate"
      @prev="prevPage"
      @next="nextPage"
    >
      <template #filters>
        <NgbDocumentPeriodFilter
          :from-month="periodFromMonth"
          :to-month="periodToMonth"
          :disabled="loading"
          @update:fromMonth="periodFromMonth = $event"
          @update:toMonth="periodToMonth = $event"
        />

        <NgbRecycleBinFilter v-model="trashMode" :disabled="loading" />
      </template>
    </NgbEntityListPageHeader>

    <div class="flex flex-1 min-h-0 flex-col gap-4 bg-ngb-bg p-6">
      <div v-if="error" class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100">
        {{ error }}
      </div>

      <NgbRegisterGrid
        class="flex-1 min-h-0"
        fill-height
        :show-panel="false"
        :columns="columns"
        :rows="rows"
        :show-totals="false"
        :storage-key="resolvedStorageKey"
        :activate-on-row-click="true"
        @rowActivate="openEdit(String($event))"
      />

      <div v-if="loading" class="text-sm text-ngb-muted">Loading…</div>
    </div>
  </div>
</template>
