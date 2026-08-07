<template>
  <div class="flex min-h-full flex-col">
    <div class="flex items-center gap-2 border-b border-ngb-border pb-3">
      <div
        class="flex min-w-0 flex-1 overflow-x-auto rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
        role="tablist"
        aria-label="Work Center views"
      >
        <button
          v-for="option in tabs"
          :key="option.value"
          type="button"
          role="tab"
          class="h-8 min-w-fit flex-auto whitespace-nowrap rounded-[var(--ngb-radius)] px-2 text-sm font-medium transition-colors ngb-focus"
          :class="tab === option.value
            ? 'bg-ngb-bg text-ngb-text shadow-sm'
            : 'bg-transparent text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text'"
          :aria-selected="tab === option.value"
          @click="tab = option.value"
        >
          {{ option.label }}
        </button>
      </div>
      <button class="ml-auto shrink-0 whitespace-nowrap text-sm font-semibold text-ngb-blue ngb-focus" type="button" @click="openFullPage">
        View all
      </button>
    </div>

    <div v-if="workCenter.loading.value" class="flex flex-1 items-center justify-center py-12 text-sm text-ngb-muted">
      Loading Work Center…
    </div>
    <div v-else-if="workCenter.error.value" class="rounded-[var(--ngb-radius)] border border-ngb-danger/30 bg-ngb-danger/5 p-4">
      <div class="text-sm font-semibold text-ngb-danger">Work Center is temporarily unavailable</div>
      <div class="mt-1 text-sm text-ngb-muted">{{ workCenter.error.value }}</div>
      <button class="mt-3 text-sm font-semibold text-ngb-blue ngb-focus" type="button" @click="reload">Retry</button>
    </div>
    <div v-else-if="workCenter.items.value.length === 0" class="flex flex-1 flex-col items-center justify-center py-12 text-center">
      <NgbIcon name="check-square" :size="28" class="text-ngb-muted" />
      <div class="mt-3 text-base font-semibold text-ngb-text">You’re all caught up</div>
      <div class="mt-1 text-sm text-ngb-muted">There are no tasks or notifications requiring your attention.</div>
    </div>
    <div v-else class="divide-y divide-ngb-border">
      <article
        v-for="item in workCenter.items.value"
        :key="`${item.kind}:${item.id}`"
        class="py-4"
        :class="item.isRead ? 'opacity-75' : ''"
      >
        <button type="button" class="w-full text-left ngb-focus" @click="openItem(item)">
          <div class="flex items-start gap-3">
            <span class="mt-0.5 h-2.5 w-2.5 shrink-0 rounded-full" :class="item.isRead ? 'bg-ngb-border' : 'bg-ngb-blue'" />
            <span class="min-w-0 flex-1">
              <span class="flex items-center gap-2">
                <span class="truncate text-sm font-semibold text-ngb-text">{{ item.title }}</span>
                <span class="ml-auto shrink-0 text-[11px] font-semibold" :class="workCenterItemTone(item)">
                  {{ workCenterItemBadge(item) }}
                </span>
              </span>
              <span v-if="item.description" class="mt-1 line-clamp-2 block text-sm leading-5 text-ngb-muted">
                {{ item.description }}
              </span>
              <span class="mt-2 flex items-center gap-2 text-xs text-ngb-muted">
                <span class="truncate">{{ item.source.title }}</span>
                <span>·</span>
                <span class="shrink-0">{{ formatWorkCenterTimestamp(item.sortAtUtc) }}</span>
              </span>
              <span v-if="isSnoozed(item)" class="mt-1 block text-xs font-medium text-ngb-warn">
                Snoozed until {{ formatWorkCenterTimestamp(item.snoozedUntilUtc!) }}
              </span>
            </span>
          </div>
        </button>
        <div v-if="hasItemActions(item)" class="mt-3 flex items-center gap-3 pl-5 text-xs font-semibold">
          <button v-if="canClaim(item)"
            type="button" class="text-ngb-blue ngb-focus"
            title="Assign this role task to you and mark it in progress"
            @click="claim(item)">Assign to me</button>
          <button v-if="canSnooze(item) && isSnoozed(item)" type="button" class="text-ngb-muted hover:text-ngb-text ngb-focus"
            @click="showNow(item)">Show now</button>
          <button v-if="item.kind === 'Notification'" type="button" class="text-ngb-muted hover:text-ngb-text ngb-focus"
            @click="dismiss(item)">Dismiss</button>
        </div>
      </article>
    </div>
    <div
      ref="infiniteScrollSentinel"
      data-testid="work-center-drawer-infinite-scroll-sentinel"
      class="min-h-1 px-3 text-center"
      aria-live="polite"
    >
      <button
        v-if="workCenter.loadMoreError.value"
        type="button"
        class="inline-block py-3 text-sm font-semibold text-ngb-blue ngb-focus"
        @click="workCenter.loadMore"
      >
        Couldn’t load more. Retry
      </button>
      <span v-else-if="workCenter.loadingMore.value" class="inline-block py-3 text-sm text-ngb-muted">
        Loading more…
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import NgbIcon from '../primitives/NgbIcon.vue'
import { getAuthSnapshot } from '../auth/keycloak'
import { resolveNgbDocumentActionTarget } from '../editor/config'
import { formatWorkCenterTimestamp, workCenterItemBadge, workCenterItemTone } from './presentation'
import type { WorkCenterItem, WorkCenterQuery } from './types'
import { useWorkCenter } from './useWorkCenter'
import { useWorkCenterInfiniteScroll } from './useWorkCenterInfiniteScroll'

const emit = defineEmits<{ (event: 'close'): void }>()
const props = withDefaults(defineProps<{ vertical?: string }>(), { vertical: '' })
const router = useRouter()
const workCenter = useWorkCenter()
type WorkCenterTab = NonNullable<WorkCenterQuery['tab']>
const tab = ref<WorkCenterTab>('attention')
const tabs: Array<{ value: WorkCenterTab; label: string }> = [
  { value: 'attention' as const, label: 'Needs Attention' },
  { value: 'tasks' as const, label: 'Tasks' },
  { value: 'notifications' as const, label: 'Notifications' },
  { value: 'completed' as const, label: 'Completed' },
]
const { sentinel: infiniteScrollSentinel } = useWorkCenterInfiniteScroll({
  nextCursor: workCenter.nextCursor,
  loading: workCenter.loading,
  loadingMore: workCenter.loadingMore,
  loadMoreError: workCenter.loadMoreError,
  loadMore: workCenter.loadMore,
})

async function reload() {
  const auth = getAuthSnapshot()
  if (!auth.initialized || !auth.authenticated) return
  await workCenter.load({
    tab: tab.value,
    limit: 20,
    vertical: props.vertical || null,
  }).catch(() => undefined)
}

watch(tab, () => { void reload() })
onMounted(() => { void reload() })

function itemRoute(item: WorkCenterItem): string | null {
  if (item.target) {
    return resolveNgbDocumentActionTarget(item.target, {
      documentType: item.source.resourceCode,
      documentId: item.source.entityId,
    })
  }
  if (item.source.resourceKind.toLowerCase() === 'document') {
    return `/documents/${encodeURIComponent(item.source.resourceCode)}/${encodeURIComponent(item.source.entityId)}`
  }
  return null
}

async function openItem(item: WorkCenterItem) {
  if (!item.isRead) await workCenter.markRead(item).catch(() => undefined)
  const route = itemRoute(item)
  if (route) {
    emit('close')
    await router.push(route)
  }
}

function openFullPage() {
  emit('close')
  void router.push({ path: '/work-center', query: { tab: tab.value } })
}

function claim(item: WorkCenterItem) {
  void workCenter.claim(item).catch(() => undefined)
}

function canClaim(item: WorkCenterItem): boolean {
  return item.kind === 'Task'
    && item.taskStatus !== 'Completed'
    && item.taskStatus !== 'Cancelled'
    && !!item.assignment?.isRoleAssigned
    && !item.assignment.claimedByUserId
}

function canSnooze(item: WorkCenterItem): boolean {
  return item.kind === 'Task'
    && item.taskStatus !== 'Completed'
    && item.taskStatus !== 'Cancelled'
}

function hasItemActions(item: WorkCenterItem): boolean {
  return item.kind === 'Notification'
    || (item.taskStatus !== 'Completed' && item.taskStatus !== 'Cancelled')
}

function dismiss(item: WorkCenterItem) {
  void workCenter.dismiss(item).catch(() => undefined)
}

function isSnoozed(item: WorkCenterItem): boolean {
  return item.kind === 'Task'
    && !!item.snoozedUntilUtc
    && Date.parse(item.snoozedUntilUtc) > Date.now()
}

function showNow(item: WorkCenterItem) {
  void workCenter.snooze(item, new Date().toISOString()).catch(() => undefined)
}
</script>
