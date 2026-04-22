<script setup lang="ts">
import { computed, useSlots } from 'vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbPageHeader from '../site/NgbPageHeader.vue'

const props = withDefaults(defineProps<{
  title: string
  canBack?: boolean
  itemsCount?: number | null
  total?: number | null
  loading?: boolean
  disableCreate?: boolean
  showFilter?: boolean
  disableFilter?: boolean
  filterActive?: boolean
  disablePrev?: boolean
  disableNext?: boolean
}>(), {
  showFilter: true,
})

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'refresh'): void
  (e: 'create'): void
  (e: 'filter'): void
  (e: 'prev'): void
  (e: 'next'): void
}>()

const slots = useSlots()
const hasFilters = computed(() => !!slots.filters)
const showCount = computed(() => props.itemsCount !== null && props.itemsCount !== undefined)
const subtitle = computed(() => {
  if (!showCount.value) return undefined
  const left = String(props.itemsCount)
  const right = props.total != null ? ` / ${props.total}` : ''
  return `${left}${right}`
})
</script>

<template>
  <NgbPageHeader :title="title" :can-back="!!canBack" @back="emit('back')">
    <template #secondary>
      <div v-if="subtitle" class="text-xs text-ngb-muted truncate">{{ subtitle }}</div>
    </template>
    <template #actions>
      <div class="flex items-center gap-1.5">
        <slot name="filters" />

        <div v-if="hasFilters" class="w-px h-5 bg-ngb-border mx-1" />

        <button class="ngb-iconbtn" title="Create" :disabled="!!disableCreate" @click="emit('create')">
          <NgbIcon name="plus" />
        </button>

        <button
          v-if="props.showFilter !== false"
          :class="['ngb-iconbtn', props.filterActive ? 'bg-ngb-bg text-ngb-text' : '']"
          title="Filter"
          :disabled="!!disableFilter"
          :aria-pressed="props.filterActive ? 'true' : 'false'"
          @click="emit('filter')"
        >
          <NgbIcon name="filter" />
        </button>

        <button class="ngb-iconbtn" title="Refresh" :disabled="!!loading" @click="emit('refresh')">
          <NgbIcon name="refresh" />
        </button>
        <button class="ngb-iconbtn" title="Previous" :disabled="!!disablePrev" @click="emit('prev')">
          <NgbIcon name="arrow-left" />
        </button>
        <button class="ngb-iconbtn" title="Next" :disabled="!!disableNext" @click="emit('next')">
          <NgbIcon name="arrow-right" />
        </button>
      </div>
    </template>
  </NgbPageHeader>
</template>
