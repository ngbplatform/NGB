<script setup lang="ts">
import { useSlots } from 'vue'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbRegisterGrid from '../components/register/NgbRegisterGrid.vue'
import type { RegisterColumn, RegisterDataRow } from '../components/register/registerTypes'
import NgbEntityListPageHeader from './NgbEntityListPageHeader.vue'

const props = withDefaults(defineProps<{
  title: string
  canBack?: boolean
  itemsCount?: number | null
  total?: number | null
  loading?: boolean
  error?: string | null
  warning?: string | null
  disableCreate?: boolean
  showFilter?: boolean
  disableFilter?: boolean
  filterActive?: boolean
  disablePrev?: boolean
  disableNext?: boolean
  columns: RegisterColumn[]
  rows: RegisterDataRow[]
  storageKey: string
  drawerOpen?: boolean
  drawerTitle?: string
  drawerSubtitle?: string
  drawerHideHeader?: boolean
  drawerFlushBody?: boolean
  beforeClose?: (() => boolean | Promise<boolean>) | null
}>(), {
  canBack: true,
  loading: false,
  error: null,
  warning: null,
  showFilter: true,
  disableCreate: false,
  disableFilter: false,
  filterActive: false,
  disablePrev: false,
  disableNext: false,
  drawerOpen: undefined,
  drawerTitle: '',
  drawerSubtitle: '',
  drawerHideHeader: false,
  drawerFlushBody: false,
  beforeClose: null,
})

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'refresh'): void
  (e: 'create'): void
  (e: 'filter'): void
  (e: 'prev'): void
  (e: 'next'): void
  (e: 'rowActivate', id: string): void
  (e: 'update:drawerOpen', value: boolean): void
}>()

const slots = useSlots()
</script>

<template>
  <div class="h-full min-h-0 flex flex-col">
    <NgbEntityListPageHeader
      :title="title"
      :can-back="canBack"
      :items-count="itemsCount"
      :total="total"
      :loading="loading"
      :disable-create="disableCreate"
      :show-filter="showFilter"
      :disable-filter="disableFilter"
      :filter-active="filterActive"
      :disable-prev="disablePrev"
      :disable-next="disableNext"
      @back="emit('back')"
      @refresh="emit('refresh')"
      @create="emit('create')"
      @filter="emit('filter')"
      @prev="emit('prev')"
      @next="emit('next')"
    >
      <template #filters>
        <slot name="filters" />
      </template>
    </NgbEntityListPageHeader>

    <div class="p-6 flex-1 min-h-0 flex flex-col">
      <div
        v-if="error"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div
        v-if="warning"
        class="mb-4 rounded-[var(--ngb-radius)] border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900/50 dark:bg-amber-950/20 dark:text-amber-100"
      >
        {{ warning }}
      </div>

      <slot name="beforeGrid" />

      <slot name="grid">
        <NgbRegisterGrid
          class="flex-1 min-h-0"
          fill-height
          :show-panel="false"
          :columns="columns"
          :rows="rows"
          :show-totals="false"
          :storage-key="storageKey"
          :activate-on-row-click="true"
          @rowActivate="emit('rowActivate', String($event))"
        />
      </slot>

      <div v-if="loading" class="mt-3 text-sm text-ngb-muted">Loading…</div>
    </div>

    <slot name="filterDrawer" />

    <NgbDrawer
      v-if="drawerOpen !== undefined && slots.drawerContent"
      :open="drawerOpen"
      :title="drawerTitle"
      :subtitle="drawerSubtitle"
      :hide-header="drawerHideHeader"
      :flush-body="drawerFlushBody"
      :before-close="beforeClose ?? undefined"
      @update:open="emit('update:drawerOpen', $event)"
    >
      <template v-if="slots.drawerActions" #actions>
        <slot name="drawerActions" />
      </template>

      <slot name="drawerContent" />
    </NgbDrawer>
  </div>
</template>
