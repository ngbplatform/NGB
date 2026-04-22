<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbEditorDiscardDialog from '../editor/NgbEditorDiscardDialog.vue'
import NgbEntityEditorDrawerActions from '../editor/NgbEntityEditorDrawerActions.vue'
import { buildCatalogFullPageUrl } from '../editor/catalogNavigation'
import { useEditorDrawerState } from '../editor/useEditorDrawerState'
import { useEntityEditorCommitHandlers } from '../editor/useEntityEditorCommitHandlers'
import type { EntityEditorHandle } from '../editor/types'
import { useRouteQueryEditorDrawer } from '../editor/useRouteQueryEditorDrawer'
import NgbRecycleBinFilter from './NgbRecycleBinFilter.vue'
import NgbRegisterPageLayout from './NgbRegisterPageLayout.vue'
import type { MetadataCatalogListPageProps } from './routePages'
import { useMetadataPageReloadKey, useMetadataRegisterPageData } from './useMetadataRegisterPageData'
import { useMetadataStore } from './store'
import { navigateBack } from '../router/backNavigation'
import { normalizeTrashMode, pushCleanRouteQuery, type QueryTrashMode } from '../router/queryParams'

const props = withDefaults(defineProps<MetadataCatalogListPageProps>(), {
  resolveTitle: null,
  resolveStorageKey: null,
  resolveDrawerExtraActions: null,
  handleDrawerExtraAction: null,
  backTarget: '/',
})

const route = useRoute()
const router = useRouter()
const metaStore = useMetadataStore()

const resolvedBackTarget = computed(() => String(props.backTarget ?? '').trim() || '/')
const canBack = computed(() => route.path !== resolvedBackTarget.value)
const catalogType = computed(() => String(route.params.catalogType ?? ''))
const offset = computed(() => Number(route.query.offset ?? 0) || 0)
const limit = computed(() => Number(route.query.limit ?? 50) || 50)

const trashMode = computed<QueryTrashMode>({
  get() {
    return normalizeTrashMode(route.query.trash)
  },
  set(value) {
    void pushCleanRouteQuery(route, router, {
      trash: value,
      offset: 0,
    })
  },
})

function isCatalogEditorDrawerQueryKey(key: string): boolean {
  return key === 'panel' || key === 'id'
}

const routeReloadKey = useMetadataPageReloadKey({
  route,
  entityTypeCode: catalogType,
  ignoreQueryKey: isCatalogEditorDrawerQueryKey,
})

const {
  loading,
  error,
  metadata,
  page,
  columns,
  rows,
  load,
} = useMetadataRegisterPageData({
  route,
  entityTypeCode: catalogType,
  reloadKey: routeReloadKey,
  loadMetadata: (entityTypeCode) => metaStore.ensureCatalogType(entityTypeCode),
  loadPage: ({ entityTypeCode }) =>
    props.loadPage({
      catalogType: entityTypeCode,
      offset: offset.value,
      limit: limit.value,
      search: String(route.query.search ?? '') || undefined,
      trashMode: trashMode.value,
    }),
})

const resolvedTitle = computed(() => {
  if (!metadata.value) return `Catalog: ${catalogType.value}`
  return props.resolveTitle?.(catalogType.value, metadata.value.displayName) ?? metadata.value.displayName
})

const resolvedStorageKey = computed(() =>
  String(props.resolveStorageKey?.(catalogType.value) ?? '').trim() || `ngb:catalog:${catalogType.value}`,
)

const editorRef = ref<EntityEditorHandle | null>(null)
const {
  drawerTitle,
  drawerSubtitle,
  editorFlags,
  discardOpen,
  handleEditorFlags,
  handleEditorState,
  resetDrawerHeading,
  requestDiscard,
  discardConfirm,
  discardCancel,
  beforeCloseDrawer,
} = useEditorDrawerState()

const {
  panelMode,
  currentId,
  isPanelOpen,
  openCreateDrawer: openRouteCreateDrawer,
  openEditDrawer: openRouteEditDrawer,
  closeDrawer: closeRouteDrawer,
} = useRouteQueryEditorDrawer({
  route,
  router,
  onBeforeOpen: async (_next, current) => {
    if (current.mode === null || !editorFlags.value.isDirty) return true
    return await requestDiscard()
  },
  onBeforeClose: async (current) => {
    if (current.mode === null || !editorFlags.value.isDirty) return true
    return await requestDiscard()
  },
})

async function openCreateDrawer() {
  await openRouteCreateDrawer({
    onCommit: () => {
      resetDrawerHeading()
    },
  })
}

async function openEditDrawer(id: string) {
  await openRouteEditDrawer(id, {
    onCommit: () => {
      resetDrawerHeading()
    },
  })
}

async function closeDrawer() {
  await closeRouteDrawer()
}

const {
  handleCreated,
  handleSaved,
  handleChanged,
  handleDeleted,
} = useEntityEditorCommitHandlers({
  reload: load,
  closeDrawer,
})

function nextPage() {
  void pushCleanRouteQuery(route, router, { offset: offset.value + limit.value })
}

function prevPage() {
  void pushCleanRouteQuery(route, router, { offset: Math.max(0, offset.value - limit.value) })
}

const resolvedDrawerExtraActions = computed(() =>
  props.resolveDrawerExtraActions?.({
    editorFlags: editorFlags.value,
  }) ?? [],
)

async function handleDrawerAction(action: string) {
  switch (action) {
    case 'expand':
      editorRef.value?.openFullPage()
      return
    case 'share':
      void editorRef.value?.copyShareLink()
      return
    case 'audit':
      editorRef.value?.openAuditLog()
      return
    case 'mark':
      editorRef.value?.toggleMarkForDeletion()
      return
    case 'save':
      void editorRef.value?.save()
      return
    default:
      await props.handleDrawerExtraAction?.({
        action,
        editor: editorRef.value,
      })
  }
}

const expandTo = computed(() => {
  if (panelMode.value === 'new') return buildCatalogFullPageUrl(catalogType.value)
  if (currentId.value) return buildCatalogFullPageUrl(catalogType.value, currentId.value)
  return null
})
</script>

<template>
  <NgbRegisterPageLayout
    :title="resolvedTitle"
    :can-back="canBack"
    :items-count="page ? page.items.length : null"
    :total="page?.total ?? null"
    :loading="loading"
    :error="error"
    :show-filter="false"
    :disable-prev="offset === 0"
    :disable-next="(page?.items?.length ?? 0) < limit"
    :columns="columns"
    :rows="rows"
    :storage-key="resolvedStorageKey"
    :drawer-open="isPanelOpen"
    :drawer-title="drawerTitle || (panelMode === 'new' ? `New ${metadata?.displayName ?? 'Record'}` : metadata?.displayName ?? 'Edit')"
    :drawer-subtitle="drawerSubtitle"
    :before-close="beforeCloseDrawer"
    @back="navigateBack(router, route, resolvedBackTarget)"
    @refresh="load"
    @create="() => void openCreateDrawer()"
    @prev="prevPage"
    @next="nextPage"
    @rowActivate="(id) => void openEditDrawer(String(id))"
    @update:drawerOpen="(value) => (!value ? void closeDrawer() : null)"
  >
    <template #filters>
      <NgbRecycleBinFilter v-model="trashMode" :disabled="loading" />
    </template>

    <template #drawerActions>
      <NgbEntityEditorDrawerActions
        :flags="editorFlags"
        :extra-actions="resolvedDrawerExtraActions"
        @action="(action) => void handleDrawerAction(action)"
      />
    </template>

    <template #drawerContent>
      <component
        :is="editorComponent"
        ref="editorRef"
        v-if="isPanelOpen"
        kind="catalog"
        :type-code="catalogType"
        :id="currentId"
        mode="drawer"
        :expand-to="expandTo"
        :navigate-on-create="false"
        @state="handleEditorState"
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
