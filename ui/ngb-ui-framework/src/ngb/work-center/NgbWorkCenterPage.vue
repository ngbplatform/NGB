<template>
  <div class="flex min-h-0 flex-1 flex-col bg-ngb-bg">
    <NgbPageHeader title="Work Center" can-back @back="router.back()">
      <template #secondary>
        <div class="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1 text-xs text-ngb-muted">
          <span>{{ workCenter.summary.value?.openTaskCount ?? 0 }} open tasks</span>
          <span :class="(workCenter.summary.value?.overdueTaskCount ?? 0) > 0 ? 'text-ngb-danger' : ''">
            {{ workCenter.summary.value?.overdueTaskCount ?? 0 }} overdue
          </span>
          <span>{{ workCenter.summary.value?.unreadNotificationCount ?? 0 }} unread</span>
        </div>
      </template>
      <template #actions>
        <div class="flex flex-wrap items-center justify-end gap-2">
          <div class="w-36">
            <NgbSelect
              :model-value="priority"
              :options="priorityOptions"
              variant="compact"
              aria-label="Priority"
              title="Priority"
              @update:model-value="setPriority"
            />
          </div>
          <div class="w-36">
            <NgbSelect
              :model-value="severity"
              :options="severityOptions"
              variant="compact"
              aria-label="Severity"
              title="Severity"
              @update:model-value="setSeverity"
            />
          </div>
          <label class="flex h-[26px] items-center gap-1.5 px-1 text-xs text-ngb-muted">
            <input v-model="overdueOnly" type="checkbox" class="rounded border-ngb-border" @change="reload">
            Overdue
          </label>
          <label class="flex h-[26px] items-center gap-1.5 px-1 text-xs text-ngb-muted">
            <input v-model="unreadOnly" type="checkbox" class="rounded border-ngb-border" @change="reload">
            Unread
          </label>
          <div class="mx-1 h-5 w-px bg-ngb-border" />
          <button
            type="button"
            class="ngb-iconbtn"
            title="Refresh"
            aria-label="Refresh"
            :disabled="workCenter.loading.value"
            @click="reload"
          >
            <NgbIcon name="refresh" />
          </button>
        </div>
      </template>
    </NgbPageHeader>

    <div class="min-h-0 flex-1 overflow-auto p-4 md:p-6">
      <section aria-label="Work Center feed">
        <div
          class="flex min-w-0 w-full overflow-x-auto rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          role="tablist"
          aria-label="Work Center views"
          data-testid="work-center-tabs"
        >
          <button
            v-for="option in tabs"
            :key="option.value"
            type="button"
            role="tab"
            class="h-8 shrink-0 whitespace-nowrap rounded-[var(--ngb-radius)] px-3 text-sm font-medium transition-colors ngb-focus"
            :class="tab === option.value
              ? 'bg-ngb-bg text-ngb-text shadow-sm'
              : 'bg-transparent text-ngb-muted hover:bg-ngb-bg hover:text-ngb-text'"
            :aria-selected="tab === option.value"
            @click="setTab(option.value)"
          >
            {{ option.label }}<template v-if="tabCount(option.value) !== null">
              ({{ tabCount(option.value) }})
            </template>
          </button>
        </div>

        <div class="mt-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card">
          <div v-if="workCenter.loading.value" class="p-10 text-center text-sm text-ngb-muted" role="status">Loading…</div>
          <div v-else-if="workCenter.error.value" class="p-6 text-center">
            <div class="text-sm font-semibold text-ngb-danger">Work Center is temporarily unavailable</div>
            <div class="mt-1 text-sm text-ngb-muted">{{ workCenter.error.value }}</div>
            <button type="button" class="mt-3 text-sm font-semibold text-ngb-blue ngb-focus" @click="reload">Retry</button>
          </div>
          <div v-else-if="workCenter.items.value.length === 0" class="p-10 text-center">
            <div class="text-base font-semibold text-ngb-text">You’re all caught up</div>
            <div class="mt-1 text-sm text-ngb-muted">There are no tasks or notifications matching this view.</div>
          </div>
          <div v-else class="divide-y divide-ngb-border">
            <article
              v-for="item in workCenter.items.value"
              :key="`${item.kind}:${item.id}`"
              class="grid items-center gap-3 px-4 py-3 md:grid-cols-[minmax(0,1fr)_10rem_2rem]"
            >
              <button type="button" class="min-w-0 text-left ngb-focus" @click="openItem(item)">
                <span class="flex items-center gap-2">
                  <span v-if="!item.isRead" class="h-2 w-2 shrink-0 rounded-full bg-ngb-blue" aria-label="Unread" />
                  <span class="truncate text-sm font-semibold text-ngb-text">{{ item.title }}</span>
                </span>
                <span v-if="item.description" class="mt-1 block line-clamp-2 text-sm text-ngb-muted">{{ item.description }}</span>
                <span class="mt-1 block truncate text-xs text-ngb-muted">
                  {{ item.source.title }}<template v-if="item.source.subtitle"> · {{ item.source.subtitle }}</template>
                </span>
              </button>
              <div class="min-w-0">
                <div class="text-sm font-semibold" :class="workCenterItemTone(item)">{{ workCenterItemBadge(item) }}</div>
                <div class="mt-1 text-xs text-ngb-muted">{{ formatWorkCenterTimestamp(item.sortAtUtc) }}</div>
                <div v-if="isSnoozed(item)" class="mt-1 text-xs font-medium leading-4 text-ngb-warn">
                  Snoozed until {{ formatWorkCenterTimestamp(item.snoozedUntilUtc!) }}
                </div>
              </div>
              <Menu v-if="hasItemActions(item)" as="div" class="relative flex justify-end">
                <MenuButton
                  type="button"
                  class="ngb-iconbtn h-8 w-8"
                  :title="`More actions for ${item.title}`"
                  :aria-label="`More actions for ${item.title}`"
                >
                  <NgbIcon name="more-vertical" :size="17" />
                </MenuButton>
                <MenuItems
                  class="absolute right-0 top-full z-30 mt-1 w-52 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-1.5 shadow-card focus:outline-none"
                >
                  <MenuItem
                    v-if="canClaim(item)"
                    v-slot="{ active, close }"
                    as="template"
                  >
                    <button
                      type="button"
                      class="flex w-full items-center gap-2.5 rounded-[var(--ngb-radius)] px-2.5 py-2 text-left text-sm text-ngb-text"
                      :class="active ? 'bg-ngb-bg' : ''"
                      title="Assign this role task to you and mark it in progress"
                      @click="close(); claim(item)"
                    >
                      <NgbIcon name="user" :size="16" class="text-ngb-muted" />
                      Assign to me
                    </button>
                  </MenuItem>
                  <MenuItem
                    v-if="canSnooze(item)"
                    v-slot="{ active, close }"
                    as="template"
                  >
                    <button
                      type="button"
                      class="flex w-full items-center gap-2.5 rounded-[var(--ngb-radius)] px-2.5 py-2 text-left text-sm text-ngb-text"
                      :class="active ? 'bg-ngb-bg' : ''"
                      @click="close(); isSnoozed(item) ? showNow(item) : snooze(item)"
                    >
                      <NgbIcon name="calendar-check" :size="16" class="text-ngb-muted" />
                      {{ isSnoozed(item) ? 'Show now' : 'Snooze 1 day' }}
                    </button>
                  </MenuItem>
                  <MenuItem
                    v-if="item.kind === 'Notification'"
                    v-slot="{ active, close }"
                    as="template"
                  >
                    <button
                      type="button"
                      class="flex w-full items-center gap-2.5 rounded-[var(--ngb-radius)] px-2.5 py-2 text-left text-sm text-ngb-text"
                      :class="active ? 'bg-ngb-bg' : ''"
                      @click="close(); dismiss(item)"
                    >
                      <NgbIcon name="circle-x" :size="16" class="text-ngb-muted" />
                      Dismiss
                    </button>
                  </MenuItem>
                  <div class="my-1 h-px bg-ngb-border" />
                  <MenuItem v-slot="{ active, close }" as="template">
                    <button
                      type="button"
                      class="flex w-full items-center gap-2.5 rounded-[var(--ngb-radius)] px-2.5 py-2 text-left text-sm font-medium text-ngb-blue"
                      :class="active ? 'bg-ngb-bg' : ''"
                      @click="close(); openItem(item)"
                    >
                      <NgbIcon name="open-in-new" :size="16" />
                      {{ item.primaryActionCode ? 'Take action' : 'Open source' }}
                    </button>
                  </MenuItem>
                </MenuItems>
              </Menu>
            </article>
          </div>
          <div
            ref="infiniteScrollSentinel"
            data-testid="work-center-infinite-scroll-sentinel"
            class="min-h-1 border-t border-transparent px-3 text-center"
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
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Menu, MenuButton, MenuItem, MenuItems } from '@headlessui/vue'
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbIcon from '../primitives/NgbIcon.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import NgbPageHeader from '../layout/NgbPageHeader.vue'
import {
  canClaimWorkCenterItem,
  canSnoozeWorkCenterItem,
  formatWorkCenterTimestamp,
  hasWorkCenterItemActions,
  isWorkCenterItemSnoozed,
  resolveWorkCenterItemRoute,
  workCenterItemBadge,
  workCenterItemTone,
  workCenterTabCount,
  workCenterTabs,
  type WorkCenterTab,
} from './presentation'
import type {
  NotificationSeverity,
  WorkCenterItem,
  WorkCenterPriority,
  WorkCenterQuery,
} from './types'
import { useWorkCenter } from './useWorkCenter'
import { useWorkCenterInfiniteScroll } from './useWorkCenterInfiniteScroll'

const props = withDefaults(defineProps<{
  vertical?: string
}>(), {
  vertical: '',
})

const route = useRoute()
const router = useRouter()
const workCenter = useWorkCenter({ vertical: props.vertical })
const { sentinel: infiniteScrollSentinel } = useWorkCenterInfiniteScroll({
  nextCursor: workCenter.nextCursor,
  loading: workCenter.loading,
  loadingMore: workCenter.loadingMore,
  loadMoreError: workCenter.loadMoreError,
  loadMore: workCenter.loadMore,
})
const validTabs = new Set<WorkCenterTab>(['attention', 'tasks', 'notifications', 'completed'])
const requestedTab = String(route.query.tab ?? '').toLowerCase() as WorkCenterTab
const tab = ref<WorkCenterTab>(validTabs.has(requestedTab) ? requestedTab : 'attention')
const priority = ref<WorkCenterPriority | ''>('')
const severity = ref<NotificationSeverity | ''>('')
const overdueOnly = ref(false)
const unreadOnly = ref(false)
const priorities: WorkCenterPriority[] = ['Low', 'Normal', 'High', 'Critical']
const severities: NotificationSeverity[] = ['Information', 'Success', 'Warning', 'Critical']
const priorityOptions = [
  { value: '', label: 'All', selectedLabel: 'Priority: All' },
  ...priorities.map((value) => ({ value, label: value, selectedLabel: `Priority: ${value}` })),
]
const severityOptions = [
  { value: '', label: 'All', selectedLabel: 'Severity: All' },
  ...severities.map((value) => ({ value, label: value, selectedLabel: `Severity: ${value}` })),
]
const tabs = workCenterTabs

function tabCount(value: WorkCenterTab): number | null {
  return workCenterTabCount(value, workCenter.summary.value)
}

function reload() {
  return workCenter.load({
    tab: tab.value,
    vertical: props.vertical || null,
    priority: priority.value || null,
    severity: severity.value || null,
    overdue: overdueOnly.value ? true : null,
    unread: unreadOnly.value ? true : null,
  }).catch(() => undefined)
}

function setPriority(value: unknown) {
  priority.value = priorities.includes(value as WorkCenterPriority)
    ? value as WorkCenterPriority
    : ''
  void reload()
}

function setSeverity(value: unknown) {
  severity.value = severities.includes(value as NotificationSeverity)
    ? value as NotificationSeverity
    : ''
  void reload()
}

function setTab(value: WorkCenterTab) {
  tab.value = value
  void router.replace({ query: { ...route.query, tab: value } })
  void reload()
}

async function openItem(item: WorkCenterItem) {
  if (!item.isRead) await workCenter.markRead(item).catch(() => undefined)
  const target = resolveWorkCenterItemRoute(item)
  if (target) await router.push(target)
}

function canClaim(item: WorkCenterItem): boolean {
  return canClaimWorkCenterItem(item)
}

function canSnooze(item: WorkCenterItem): boolean {
  return canSnoozeWorkCenterItem(item)
}

function hasItemActions(item: WorkCenterItem): boolean {
  return hasWorkCenterItemActions(item)
}

function claim(item: WorkCenterItem) {
  void workCenter.claim(item).catch(() => undefined)
}

function dismiss(item: WorkCenterItem) {
  void workCenter.dismiss(item).catch(() => undefined)
}

function snooze(item: WorkCenterItem) {
  void workCenter.snooze(item, new Date(Date.now() + 24 * 60 * 60 * 1_000).toISOString()).catch(() => undefined)
}

function isSnoozed(item: WorkCenterItem): boolean {
  return isWorkCenterItemSnoozed(item)
}

function showNow(item: WorkCenterItem) {
  void workCenter.snooze(item, new Date().toISOString()).catch(() => undefined)
}

onMounted(() => {
  void reload()
  void workCenter.connectRealtime()
})
</script>
