<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbBadge from '../primitives/NgbBadge.vue'
import NgbEditorDiscardDialog from '../editor/NgbEditorDiscardDialog.vue'
import { buildDocumentFullPageUrl, shouldOpenDocumentInFullPageByDefault } from '../editor/documentNavigation'
import { isDocumentEditorDrawerQueryKey, useDocumentEditorDrawerState } from '../editor/useDocumentEditorDrawerState'
import { useEditorDrawerState } from '../editor/useEditorDrawerState'
import { useEntityEditorCommitHandlers } from '../editor/useEntityEditorCommitHandlers'
import type { EntityEditorHandle } from '../editor/types'
import { useLookupStore } from '../lookup/store'
import NgbDocumentListFiltersDrawer from './NgbDocumentListFiltersDrawer.vue'
import NgbDocumentPeriodFilter from './NgbDocumentPeriodFilter.vue'
import NgbRecycleBinFilter from './NgbRecycleBinFilter.vue'
import NgbRegisterPageLayout from './NgbRegisterPageLayout.vue'
import type { MetadataDocumentListPageProps } from './routePages'
import {
  monthValueToDateOnlyEnd,
  monthValueToDateOnlyStart,
  useMonthPagedListQuery,
} from './monthPagedListQuery'
import { useMetadataStore } from './store'
import { useMetadataListFilters } from './useMetadataListFilters'
import {
  useMetadataPageReloadKey,
  useMetadataRegisterPageData,
} from './useMetadataRegisterPageData'
import { currentRouteBackTarget, navigateBack, withBackTarget } from '../router/backNavigation'
import { normalizeSingleQueryValue } from '../router/queryParams'

const props = withDefaults(defineProps<MetadataDocumentListPageProps>(), {
  resolveLookupHint: null,
  resolveTitle: null,
  resolveStorageKey: null,
  resolveWarning: null,
  isCreateDisabled: null,
  handleCreateOverride: null,
  backTarget: '/',
})

const route = useRoute()
const router = useRouter()
const metaStore = useMetadataStore()
const lookupStore = useLookupStore()

const resolvedBackTarget = computed(() => String(props.backTarget ?? '').trim() || '/')
const canBack = computed(() => route.path !== resolvedBackTarget.value)
const documentType = computed(() => String(route.params.documentType ?? ''))

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

const filterDrawerOpen = ref(false)

const routeReloadKey = useMetadataPageReloadKey({
  route,
  entityTypeCode: documentType,
  ignoreQueryKey: isDocumentEditorDrawerQueryKey,
})

const {
  loading,
  error,
  metadata,
  page,
  listFilters,
  hasListFilters,
  columns,
  rows,
  load,
} = useMetadataRegisterPageData({
  route,
  entityTypeCode: documentType,
  reloadKey: routeReloadKey,
  loadMetadata: (entityTypeCode) => metaStore.ensureDocumentType(entityTypeCode),
  loadPage: async ({ entityTypeCode, metadata }) => {
    const listFilterParams = Object.fromEntries(
      (metadata.list?.filters ?? [])
        .map((field) => [field.key, normalizeSingleQueryValue(route.query[field.key])] as const)
        .filter((entry) => entry[1].length > 0),
    )

    return await props.loadPage({
      documentType: entityTypeCode,
      metadata,
      offset: offset.value,
      limit: limit.value,
      search: String(route.query.search ?? '') || undefined,
      trashMode: trashMode.value,
      periodFrom: monthValueToDateOnlyStart(periodFromMonth.value) ?? null,
      periodTo: monthValueToDateOnlyEnd(periodToMonth.value) ?? null,
      listFilters: listFilterParams,
    })
  },
  lookupStore,
  resolveLookupHint: props.resolveLookupHint
    ? ({ entityTypeCode, fieldKey, lookup }) => props.resolveLookupHint?.({ entityTypeCode, fieldKey, lookup }) ?? null
    : undefined,
})

const preferFullPage = computed(() => shouldOpenDocumentInFullPageByDefault(metadata.value))
const resolvedTitle = computed(() => {
  if (!metadata.value) return `Documents: ${documentType.value}`
  return props.resolveTitle?.(documentType.value, metadata.value.displayName) ?? metadata.value.displayName
})
const resolvedStorageKey = computed(() =>
  String(props.resolveStorageKey?.(documentType.value) ?? '').trim() || `ngb:document:${documentType.value}`,
)
const resolvedWarning = computed(() => props.resolveWarning?.(documentType.value, metadata.value) ?? null)
const createDisabled = computed(() => props.isCreateDisabled?.(documentType.value, metadata.value) ?? false)

const {
  filterDraft,
  lookupItemsByFilterKey,
  activeFilterBadges,
  hasActiveFilters,
  canUndoFilters,
  handleLookupQuery,
  handleItemsUpdate,
  handleValueUpdate,
  undo,
} = useMetadataListFilters({
  route,
  router,
  entityTypeCode: documentType,
  filters: listFilters,
  lookupStore,
  resolveLookupHint: ({ entityTypeCode, field }) =>
    props.resolveLookupHint?.({
      entityTypeCode,
      fieldKey: field.key,
      lookup: field.lookup,
    }) ?? null,
})

const {
  isPanelOpen,
  currentEditorId,
  initialFields,
  initialParts,
  expandTo,
  openCreateDrawer,
  openEditDrawer,
  reopenCreatedDocument,
  closeDrawer,
} = useDocumentEditorDrawerState({
  route,
  router,
  documentType,
})

const editorRef = ref<EntityEditorHandle | null>(null)
const {
  editorFlags,
  discardOpen,
  requestDiscard,
  handleEditorFlags,
  discardConfirm,
  discardCancel,
  beforeCloseDrawer,
} = useEditorDrawerState()

async function requestDrawerReplacement(): Promise<boolean> {
  if (!isPanelOpen.value || !editorFlags.value.isDirty) return true
  return await requestDiscard()
}

async function openCreateDefault() {
  if (preferFullPage.value) {
    await router.push(
      withBackTarget(
        buildDocumentFullPageUrl(documentType.value),
        currentRouteBackTarget(route),
      ),
    )
    return
  }

  if (isPanelOpen.value && !currentEditorId.value) return
  if (!await requestDrawerReplacement()) return
  openCreateDrawer()
}

async function openEdit(id: string) {
  if (preferFullPage.value) {
    void router.push(
      withBackTarget(
        buildDocumentFullPageUrl(documentType.value, id),
        currentRouteBackTarget(route),
      ),
    )
    return
  }

  if (currentEditorId.value === id) return
  if (!await requestDrawerReplacement()) return
  openEditDrawer(id)
}

const {
  handleCreated,
  handleSaved,
  handleChanged,
  handleDeleted,
} = useEntityEditorCommitHandlers({
  reload: load,
  closeDrawer,
  closeOnCreated: false,
  closeOnSaved: false,
  closeOnChanged: ({ reason }) => reason === 'markForDeletion' || reason === 'unmarkForDeletion',
  onCreated: async ({ id }) => {
    reopenCreatedDocument(id)
  },
})

watch(
  () => documentType.value,
  () => {
    filterDrawerOpen.value = false
  },
)

async function createNew() {
  const handled = await props.handleCreateOverride?.({
    documentType: documentType.value,
    metadata: metadata.value,
    preferFullPage: preferFullPage.value,
    route,
    router,
    openCreateDrawer,
    openFullPage: async () => {
      await router.push(
        withBackTarget(
          buildDocumentFullPageUrl(documentType.value),
          currentRouteBackTarget(route),
        ),
      )
    },
  })

  if (handled) return
  await openCreateDefault()
}
</script>

<template>
  <NgbRegisterPageLayout
    :title="resolvedTitle"
    :can-back="canBack"
    :items-count="page ? page.items.length : null"
    :total="page?.total ?? null"
    :loading="loading"
    :error="error"
    :warning="resolvedWarning"
    :disable-create="createDisabled"
    :disable-filter="!hasListFilters"
    :filter-active="hasActiveFilters"
    :disable-prev="offset === 0"
    :disable-next="(page?.items?.length ?? 0) < limit"
    :columns="columns"
    :rows="rows"
    :storage-key="resolvedStorageKey"
    :drawer-open="isPanelOpen"
    drawer-title=""
    drawer-hide-header
    drawer-flush-body
    :before-close="beforeCloseDrawer"
    @back="navigateBack(router, route, resolvedBackTarget)"
    @refresh="load"
    @create="() => void createNew()"
    @filter="filterDrawerOpen = true"
    @prev="prevPage"
    @next="nextPage"
    @rowActivate="openEdit"
    @update:drawerOpen="(value) => (!value ? closeDrawer() : null)"
  >
    <template #filters>
      <NgbDocumentPeriodFilter
        :from-month="periodFromMonth"
        :to-month="periodToMonth"
        :disabled="loading"
        @update:from-month="periodFromMonth = $event"
        @update:to-month="periodToMonth = $event"
      />
      <NgbRecycleBinFilter v-model="trashMode" :disabled="loading" />
    </template>

    <template #beforeGrid>
      <div v-if="activeFilterBadges.length > 0" class="mb-4 flex flex-wrap items-center gap-2">
        <NgbBadge v-for="badge in activeFilterBadges" :key="badge.key" tone="neutral">
          {{ badge.text }}
        </NgbBadge>
      </div>
    </template>

    <template #filterDrawer>
      <NgbDocumentListFiltersDrawer
        :open="filterDrawerOpen"
        :filters="listFilters"
        :values="filterDraft"
        :lookup-items-by-key="lookupItemsByFilterKey"
        :can-undo="canUndoFilters"
        :disabled="loading"
        @update:open="filterDrawerOpen = $event"
        @lookup-query="handleLookupQuery"
        @update:items="handleItemsUpdate"
        @update:value="handleValueUpdate"
        @undo="undo"
      />
    </template>

    <template #drawerContent>
      <component
        :is="editorComponent"
        ref="editorRef"
        v-if="isPanelOpen"
        kind="document"
        :type-code="documentType"
        :id="currentEditorId"
        mode="drawer"
        :initial-fields="initialFields"
        :initial-parts="initialParts"
        :expand-to="expandTo"
        :navigate-on-create="false"
        @flags="handleEditorFlags"
        @created="handleCreated"
        @saved="handleSaved"
        @changed="handleChanged"
        @deleted="handleDeleted"
        @close="closeDrawer"
      />
    </template>
  </NgbRegisterPageLayout>

  <NgbEditorDiscardDialog
    :open="discardOpen"
    @cancel="discardCancel"
    @confirm="discardConfirm"
  />
</template>
