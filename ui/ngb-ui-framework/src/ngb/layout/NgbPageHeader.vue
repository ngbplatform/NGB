<template>
  <div
    class="border-b border-ngb-border bg-ngb-card"
    :class="variant === 'compact' ? 'px-4 py-3 sm:px-6' : 'px-4 py-4 sm:px-6'"
  >
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
      <div class="flex min-w-0 items-start gap-3 sm:flex-1 sm:items-center">
        <button
          v-if="canBack"
          class="ngb-iconbtn"
          type="button"
          title="Back"
          aria-label="Back"
          @click="emit('back')"
        >
          <NgbIcon name="arrow-left" />
        </button>

        <div class="min-w-0 flex-1 self-center">
          <div v-if="variant !== 'compact' && breadcrumbs?.length" class="mb-1 truncate text-xs text-ngb-muted">
            <span v-for="(breadcrumb, index) in breadcrumbs" :key="index">
              <span>{{ breadcrumb }}</span><span v-if="index < breadcrumbs.length - 1"> / </span>
            </span>
          </div>
          <h1 class="truncate font-semibold" :class="variant === 'compact' ? 'text-base' : 'text-lg'">{{ title }}</h1>
          <div v-if="variant !== 'compact'" class="mt-1 flex min-h-[1.25rem] min-w-0 items-center">
            <slot name="secondary" />
          </div>
        </div>
      </div>

      <div class="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:justify-end">
        <slot name="actions" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import NgbIcon from '../primitives/NgbIcon.vue'

defineProps<{
  title: string
  breadcrumbs?: string[]
  canBack?: boolean
  variant?: 'default' | 'compact'
}>()

const emit = defineEmits<{
  (event: 'back'): void
}>()
</script>
