<template>
  <Teleport to="body">
    <TransitionRoot appear :show="store.isOpen" as="template" @after-leave="restoreLastFocus">
      <Dialog as="div" class="relative z-[90]" :initialFocus="inputRef" @close="store.close()">
        <TransitionChild
          as="template"
          enter="duration-150 ease-out"
          enter-from="opacity-0"
          enter-to="opacity-100"
          leave="duration-120 ease-in"
          leave-from="opacity-100"
          leave-to="opacity-0"
        >
          <div class="fixed inset-0 bg-[rgba(7,12,20,.48)] backdrop-blur-[2px]" />
        </TransitionChild>

        <div class="fixed inset-0 overflow-y-auto">
          <div class="flex min-h-full items-start justify-center px-4 pb-8 pt-[10vh]">
            <TransitionChild
              as="template"
              enter="duration-150 ease-out"
              enter-from="opacity-0 translate-y-3 scale-[0.985]"
              enter-to="opacity-100 translate-y-0 scale-100"
              leave="duration-120 ease-in"
              leave-from="opacity-100 translate-y-0 scale-100"
              leave-to="opacity-0 translate-y-3 scale-[0.985]"
            >
              <DialogPanel data-testid="command-palette-dialog" class="w-full max-w-[860px] overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card">
                <DialogTitle class="sr-only">Command palette</DialogTitle>

                <div class="border-b border-ngb-border px-4 py-3">
                  <div class="flex h-10 items-center gap-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3">
                    <span class="text-ngb-muted">
                      <NgbIcon name="search" :size="18" />
                    </span>

                    <input
                      ref="inputRef"
                      data-testid="command-palette-input"
                      :value="store.query"
                      class="h-full flex-1 border-0 bg-transparent px-0 text-sm text-ngb-text outline-none placeholder:text-ngb-muted/70"
                      :placeholder="placeholder"
                      role="combobox"
                      aria-autocomplete="list"
                      :aria-expanded="store.hasResults"
                      :aria-controls="listboxId"
                      :aria-activedescendant="activeDescendantId"
                      @input="store.setQuery(String(($event.target as HTMLInputElement)?.value ?? ''))"
                      @keydown="onInputKeyDown"
                    />

                    <span class="hidden items-center gap-1 md:flex">
                      <template v-if="isMac">
                        <span class="ngb-kbd">⌘</span>
                      </template>
                      <template v-else>
                        <span class="ngb-kbd">Ctrl</span>
                      </template>
                      <span class="ngb-kbd">K</span>
                    </span>
                  </div>

                  <div v-if="scopeBadgeLabel || store.showRemoteLoading" class="mt-2 flex flex-wrap items-center gap-2 text-xs text-ngb-muted">
                    <span v-if="scopeBadgeLabel" class="rounded-full border border-ngb-border bg-ngb-bg px-2.5 py-1">
                      {{ scopeBadgeLabel }}
                    </span>
                    <span v-if="store.showRemoteLoading" class="rounded-full border border-ngb-border bg-ngb-bg px-2.5 py-1">
                      Updating remote results…
                    </span>
                  </div>
                </div>

                <div class="sr-only" aria-live="polite">
                  {{ liveRegionText }}
                </div>

                <div class="max-h-[560px] overflow-y-auto p-2" :id="listboxId" role="listbox">
                  <div
                    v-if="store.hasRemoteError && store.groups.length > 0"
                    class="mx-2 mb-2 rounded-[var(--ngb-radius)] border border-[rgba(185,28,28,.16)] bg-[rgba(254,242,242,.92)] px-3 py-2 text-xs text-[#7f1d1d]"
                  >
                    {{ store.remoteError }}
                  </div>

                  <template v-if="store.groups.length > 0">
                    <section v-for="group in store.groups" :key="group.code" class="py-1.5">
                      <div class="px-3 pb-2 pt-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-ngb-muted">
                        {{ group.label }}
                      </div>

                      <div class="space-y-1">
                        <button
                          v-for="item in group.items"
                          :id="optionId(item.key)"
                          :key="item.key"
                          :data-option-id="optionId(item.key)"
                          :data-item-key="item.key"
                          type="button"
                          role="option"
                          :aria-selected="isActive(item.key)"
                          class="flex w-full items-start gap-3 rounded-[var(--ngb-radius)] border px-3 py-2.5 text-left transition-colors"
                          :class="isActive(item.key)
                            ? 'border-[rgba(11,60,93,.22)] bg-[rgba(11,60,93,.08)]'
                            : 'border-transparent hover:border-ngb-border hover:bg-ngb-bg'"
                          @mouseenter="setActive(item.key)"
                          @click="onItemClick(item, $event)"
                        >
                          <span
                            class="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-[var(--ngb-radius)]"
                            :class="isActive(item.key) ? 'bg-transparent text-ngb-text' : 'bg-transparent text-ngb-muted'"
                          >
                            <NgbIcon :name="resolveCommandPaletteIcon(item.icon)" :size="17" />
                          </span>

                          <span class="min-w-0 flex-1">
                            <span class="flex items-start justify-between gap-3">
                              <span class="min-w-0">
                                <span class="block truncate text-sm font-semibold text-ngb-text">{{ item.title }}</span>
                                <span class="mt-0.5 block truncate text-xs leading-5 text-ngb-muted">{{ resolveCommandPaletteSubtitle(item) }}</span>
                              </span>

                              <span class="flex shrink-0 items-center gap-2">
                                <NgbBadge
                                  v-if="resolveCommandPaletteBadge(item)"
                                  tone="neutral"
                                >
                                  {{ resolveCommandPaletteBadge(item) }}
                                </NgbBadge>
                                <span v-if="item.openInNewTabSupported" class="hidden text-[11px] text-ngb-muted lg:inline">
                                  {{ primaryModifier }}+Enter
                                </span>
                              </span>
                            </span>
                          </span>
                        </button>
                      </div>
                    </section>
                  </template>

                  <NgbCommandPaletteEmptyState
                    v-else
                    :query="store.cleanQuery"
                    :loading="store.showRemoteLoading"
                    :error="store.hasRemoteError ? store.remoteError : null"
                  />
                </div>

                <NgbCommandPaletteFooterHints :primary-modifier="primaryModifier" />
              </DialogPanel>
            </TransitionChild>
          </div>
        </div>
      </Dialog>
    </TransitionRoot>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Dialog, DialogPanel, DialogTitle, TransitionChild, TransitionRoot } from '@headlessui/vue'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import { useCommandPaletteStore } from './store'
import type { CommandPaletteItem } from './types'
import {
  resolveCommandPaletteBadge,
  resolveCommandPaletteIcon,
  resolveCommandPaletteSubtitle,
} from './presentation'
import NgbCommandPaletteEmptyState from './NgbCommandPaletteEmptyState.vue'
import NgbCommandPaletteFooterHints from './NgbCommandPaletteFooterHints.vue'

const store = useCommandPaletteStore()
const inputRef = ref<HTMLInputElement | null>(null)
const lastFocusedElement = ref<HTMLElement | null>(null)
let restoreFocusTimer: ReturnType<typeof window.setTimeout> | null = null

const listboxId = 'ngb-command-palette-listbox'
const placeholder = 'Search pages, records, reports, or run a command…'
const isMac = /Mac|iPhone|iPad|iPod/i.test(String(globalThis.navigator?.platform ?? ''))
const primaryModifier = computed(() => (isMac ? '⌘' : 'Ctrl'))

function onDocumentFocusIn(event: FocusEvent): void {
  if (store.isOpen) return
  const target = event.target instanceof HTMLElement ? event.target : null
  if (!target || target === document.body) return
  lastFocusedElement.value = target
}

const flatItems = computed(() => store.flatItems)
const indexByKey = computed(() => {
  const map = new Map<string, number>()
  flatItems.value.forEach((item, index) => map.set(item.key, index))
  return map
})

const activeItem = computed(() => flatItems.value[store.activeIndex] ?? null)
const activeDescendantId = computed(() => (activeItem.value ? optionId(activeItem.value.key) : undefined))
const scopeBadgeLabel = computed<string | null>(() => {
  switch (store.activeScope) {
    case 'commands':
      return 'Commands'
    case 'pages':
      return 'Pages'
    case 'reports':
      return 'Reports'
    case 'documents':
      return 'Documents'
    case 'catalogs':
      return 'Catalogs'
    default:
      return null
  }
})

const liveRegionText = computed(() => {
  if (store.hasRemoteError) return store.remoteError ?? 'Remote search is unavailable.'
  if (store.showRemoteLoading) return 'Updating remote results.'
  if (store.groups.length === 0) return store.cleanQuery ? `No results for ${store.cleanQuery}.` : 'Showing command palette shortcuts.'
  return `${flatItems.value.length} results available.`
})

watch(
  () => store.isOpen,
  async (open) => {
    if (open) {
      if (restoreFocusTimer) {
        window.clearTimeout(restoreFocusTimer)
        restoreFocusTimer = null
      }

      const activeElement = document.activeElement
      if (activeElement instanceof HTMLElement && activeElement !== document.body)
        lastFocusedElement.value = activeElement
      await nextTick()
      focusInput(true)
      return
    }

    const restoreTarget = lastFocusedElement.value
    restoreFocusTimer = window.setTimeout(() => {
      restoreFocusTimer = null
      if (store.isOpen) return
      restoreTarget?.focus?.()
    }, 180)
  },
)

watch(
  () => store.focusRequestKey,
  async () => {
    if (!store.isOpen) return
    await nextTick()
    focusInput(true)
  },
)

watch(
  () => activeDescendantId.value,
  async (id) => {
    if (!id) return
    await nextTick()
    document.querySelector<HTMLElement>(`[data-option-id="${id}"]`)?.scrollIntoView({ block: 'nearest' })
  },
)

function focusInput(selectAll = false, attempt = 0): void {
  inputRef.value?.focus()
  if (selectAll) inputRef.value?.select()
  window.setTimeout(() => {
    if (!store.isOpen) return
    if (document.activeElement === inputRef.value) return
    if (attempt >= 4) return
    focusInput(selectAll, attempt + 1)
  }, attempt === 0 ? 0 : 16)
}

function restoreLastFocus(): void {
  if (store.isOpen) return
  if (restoreFocusTimer) {
    window.clearTimeout(restoreFocusTimer)
    restoreFocusTimer = null
  }
  window.setTimeout(() => {
    if (store.isOpen) return
    lastFocusedElement.value?.focus?.()
  }, 0)
}

onMounted(() => {
  document.addEventListener('focusin', onDocumentFocusIn, true)
})

onBeforeUnmount(() => {
  document.removeEventListener('focusin', onDocumentFocusIn, true)
})

function isActive(key: string): boolean {
  return activeItem.value?.key === key
}

function setActive(key: string): void {
  const index = indexByKey.value.get(key)
  if (index == null) return
  store.setActiveIndex(index)
}

function optionId(key: string): string {
  return `ngb-command-palette-option-${key.replace(/[^a-z0-9_-]+/gi, '-')}`
}

function onInputKeyDown(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    store.moveActive(1)
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    store.moveActive(-1)
    return
  }

  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    store.close()
    return
  }

  if (event.key === 'Enter') {
    event.preventDefault()
    void store.executeActive((event.metaKey || event.ctrlKey) ? 'new-tab' : 'default')
  }
}

function onItemClick(item: CommandPaletteItem, event: MouseEvent): void {
  const mode = (event.metaKey || event.ctrlKey) && item.openInNewTabSupported ? 'new-tab' : 'default'
  void store.executeItem(item, mode)
}
</script>
