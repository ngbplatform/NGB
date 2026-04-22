<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbRegisterGrid from '../components/register/NgbRegisterGrid.vue'
import NgbEditorDiscardDialog from '../editor/NgbEditorDiscardDialog.vue'
import { useEntityEditorCommitHandlers } from '../editor/useEntityEditorCommitHandlers'
import NgbEntityEditorDrawerActions from '../editor/NgbEntityEditorDrawerActions.vue'
import { useEditorDrawerState } from '../editor/useEditorDrawerState'
import { useRouteQueryEditorDrawer } from '../editor/useRouteQueryEditorDrawer'
import NgbRegisterPageLayout from '../metadata/NgbRegisterPageLayout.vue'
import NgbRecycleBinFilter from '../metadata/NgbRecycleBinFilter.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import { navigateBack } from '../router/backNavigation'
import {
  normalizeSingleQueryValue,
  normalizeTrashMode,
  pushCleanRouteQuery,
  replaceCleanRouteQuery,
  type QueryTrashMode,
} from '../router/queryParams'
import { toErrorMessage } from '../utils/errorMessage'
import { getChartOfAccountsMetadata, getChartOfAccountsPage } from './api'
import NgbChartOfAccountEditor from './NgbChartOfAccountEditor.vue'
import type {
  ChartOfAccountsAccountDto,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsPageDto,
  ChartOfAccountEditorShellState,
} from './types'

type ChartOfAccountEditorHandle = {
  save: () => Promise<void>
  copyShareLink: () => Promise<boolean>
  openAuditLog: () => void
  closeAuditLog: () => void
  toggleMarkForDeletion: () => void
}

const DEFAULT_EDITOR_SHELL: ChartOfAccountEditorShellState = {
  hideHeader: false,
  flushBody: false,
}

const props = withDefaults(defineProps<{
  title?: string
  backTarget?: string | null
  storageKey?: string | null
  routeBasePath?: string | null
}>(), {
  title: 'Chart of Accounts',
  backTarget: '/',
  storageKey: null,
  routeBasePath: null,
})

const route = useRoute()
const router = useRouter()

watch(
  () => [route.query.id, route.query.accountId, route.query.panel],
  ([idValue, legacyAccountId, panelValue]) => {
    const legacyId = normalizeSingleQueryValue(legacyAccountId)
    if (!legacyId) return

    const id = normalizeSingleQueryValue(idValue)
    if (id) {
      void replaceCleanRouteQuery(route, router, { accountId: undefined })
      return
    }

    const panel = normalizeSingleQueryValue(panelValue)
    void replaceCleanRouteQuery(route, router, {
      id: legacyId,
      accountId: undefined,
      panel: panel || 'edit',
    })
  },
  { immediate: true },
)

const resolvedBackTarget = computed(() => String(props.backTarget ?? '').trim() || '/')
const resolvedRouteBasePath = computed(() => String(props.routeBasePath ?? '').trim() || route.path)
const resolvedStorageKey = computed(() => String(props.storageKey ?? '').trim() || `ngb:accounting:chart-of-accounts:${resolvedRouteBasePath.value}`)
const canBack = computed(() => route.path !== resolvedBackTarget.value)
const offset = computed(() => Number(route.query.offset ?? 0) || 0)
const limit = computed(() => Number(route.query.limit ?? 50) || 50)

const trashMode = computed<QueryTrashMode>({
  get: () => normalizeTrashMode(route.query.trash),
  set: (value) => {
    void replaceCleanRouteQuery(route, router, {
      trash: value,
      offset: 0,
    })
  },
})

const loading = ref(false)
const error = ref<string | null>(null)
const page = ref<ChartOfAccountsPageDto | null>(null)
const metadata = ref<ChartOfAccountsMetadataDto | null>(null)
let loadSequence = 0

const columns = computed(() => [
  { key: 'code', title: 'Code', width: 120, align: 'left' as const, sortable: true },
  { key: 'name', title: 'Name', align: 'left' as const, sortable: true },
  { key: 'accountType', title: 'Type', width: 140, align: 'left' as const, sortable: true },
  { key: 'cashFlowRole', title: 'Cash Flow', width: 180, align: 'left' as const, sortable: true },
  {
    key: 'isActive',
    title: 'Active',
    width: 90,
    align: 'center' as const,
    sortable: true,
    format: (value: unknown) => (value ? 'Yes' : 'No'),
  },
])

const cashFlowRoleLabelByValue = computed(() => {
  const entries = (metadata.value?.cashFlowRoleOptions ?? []).map((option) => [option.value, option.label] as const)
  return new Map(entries)
})

const rows = computed(() => {
  const items = page.value?.items ?? []
  return items.map((account: ChartOfAccountsAccountDto) => ({
    key: account.accountId,
    __status: (account.isDeleted || account.isMarkedForDeletion) ? 'marked' : (account.isActive ? 'active' : 'saved'),
    isDeleted: account.isDeleted,
    isMarkedForDeletion: account.isMarkedForDeletion,
    code: account.code,
    name: account.name,
    accountType: account.accountType,
    cashFlowRole: cashFlowRoleLabelByValue.value.get(account.cashFlowRole ?? '') ?? '',
    isActive: account.isActive,
  }))
})

const coaGridKey = computed(() => [
  trashMode.value,
  String(offset.value),
  String(limit.value),
  String(page.value?.items?.length ?? 0),
].join('|'))

async function ensureMetadataLoaded(): Promise<void> {
  if (metadata.value) return
  metadata.value = await getChartOfAccountsMetadata()
}

async function load(): Promise<boolean> {
  const seq = ++loadSequence
  loading.value = true
  error.value = null

  try {
    await ensureMetadataLoaded()

    const mode = trashMode.value
    const includeDeleted = mode !== 'active'
    const onlyDeleted = mode === 'deleted' ? true : null

    const nextPage = await getChartOfAccountsPage({
      offset: offset.value,
      limit: limit.value,
      search: null,
      onlyActive: null,
      includeDeleted,
      onlyDeleted,
    })
    if (seq !== loadSequence) return false

    page.value = nextPage

    return true
  } catch (cause) {
    if (seq !== loadSequence) return false
    error.value = toErrorMessage(cause, 'Failed to load the chart of accounts.')
    return false
  } finally {
    if (seq === loadSequence) loading.value = false
  }
}

watch(
  () => [route.query.offset, route.query.limit, route.query.trash],
  () => {
    void load()
  },
  { immediate: true },
)

function nextPage() {
  void pushCleanRouteQuery(route, router, { offset: offset.value + limit.value })
}

function prevPage() {
  void pushCleanRouteQuery(route, router, { offset: Math.max(0, offset.value - limit.value) })
}

const editorRef = ref<ChartOfAccountEditorHandle | null>(null)
const editorShell = ref<ChartOfAccountEditorShellState>({ ...DEFAULT_EDITOR_SHELL })
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

function resetEditorShell() {
  editorShell.value = { ...DEFAULT_EDITOR_SHELL }
}

function handleEditorShell(next: ChartOfAccountEditorShellState) {
  editorShell.value = { ...next }
}

async function beforeRouteDrawerClose(): Promise<boolean> {
  if (editorShell.value.hideHeader) {
    editorRef.value?.closeAuditLog()
    return false
  }

  if (!editorFlags.value.isDirty) return true
  return await requestDiscard()
}

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
  idImpliesEdit: true,
  clearKeys: ['accountId'],
  onBeforeOpen: async (_next, current) => {
    if (editorShell.value.hideHeader) editorRef.value?.closeAuditLog()
    if (current.mode === null || !editorFlags.value.isDirty) return true
    return await requestDiscard()
  },
  onBeforeClose: async () => await beforeRouteDrawerClose(),
})

async function openCreateDrawer() {
  await openRouteCreateDrawer({
    onCommit: () => {
      resetDrawerHeading()
      resetEditorShell()
    },
  })
}

async function openEditDrawer(id: string) {
  await openRouteEditDrawer(id, {
    onCommit: () => {
      resetDrawerHeading()
      resetEditorShell()
    },
  })
}

function closeDrawer() {
  void closeRouteDrawer({
    onCommit: () => {
      resetEditorShell()
    },
  })
}

const {
  handleCreated,
  handleSaved,
  handleChanged,
} = useEntityEditorCommitHandlers({
  reload: load,
  closeDrawer,
  closeOnCreated: ({ reloadSucceeded }) => reloadSucceeded,
  onCreated: async ({ id, reloadSucceeded }) => {
    if (reloadSucceeded) return

    resetEditorShell()
    resetDrawerHeading()
    void replaceCleanRouteQuery(route, router, {
      panel: 'edit',
      id,
      accountId: undefined,
    })
  },
})

async function beforeCloseChartOfAccountsDrawer(): Promise<boolean> {
  if (editorShell.value.hideHeader) return await beforeRouteDrawerClose()
  return await beforeCloseDrawer()
}

function handleDrawerAction(action: string) {
  switch (action) {
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
  }
}
</script>

<template>
  <NgbRegisterPageLayout
    :title="title"
    :can-back="canBack"
    :items-count="page ? page.items.length : null"
    :total="page?.total ?? null"
    :loading="loading"
    :error="error"
    :show-filter="false"
    :disable-prev="offset === 0"
    :disable-next="(page?.items?.length ?? 0) < limit"
    :disable-create="loading"
    :columns="columns"
    :rows="rows"
    :storage-key="resolvedStorageKey"
    :drawer-open="isPanelOpen"
    :drawer-title="drawerTitle || (panelMode === 'new' ? 'New account' : 'Account')"
    :drawer-subtitle="drawerSubtitle"
    :drawer-hide-header="editorShell.hideHeader"
    :drawer-flush-body="editorShell.flushBody"
    :before-close="beforeCloseChartOfAccountsDrawer"
    @back="navigateBack(router, route, resolvedBackTarget)"
    @refresh="load"
    @create="openCreateDrawer"
    @prev="prevPage"
    @next="nextPage"
    @rowActivate="openEditDrawer"
    @update:drawerOpen="(value) => (!value ? closeDrawer() : null)"
  >
    <template #filters>
      <NgbRecycleBinFilter v-model="trashMode" :disabled="loading" />
    </template>

    <template #grid>
      <NgbRegisterGrid
        class="flex-1 min-h-0"
        :key="coaGridKey"
        fill-height
        :show-panel="false"
        :columns="columns"
        :rows="rows"
        :group-by="['accountType']"
        :default-expanded="true"
        :storage-key="resolvedStorageKey"
        :show-status-column="true"
        :show-group-counts="true"
        :show-group-toggle-icons="true"
        :show-totals="false"
        activate-on-row-click
        @rowActivate="(id) => openEditDrawer(String(id))"
      >
        <template #statusHeader="{ hasGroups, allGroupsExpanded, toggleAllGroups }">
          <button
            v-if="hasGroups"
            class="flex items-center justify-center rounded-[var(--ngb-radius)] border-0 bg-transparent p-0 text-ngb-muted hover:text-ngb-text ngb-focus"
            :title="allGroupsExpanded ? 'Collapse all' : 'Expand all'"
            @click.stop="toggleAllGroups()"
          >
            <NgbIcon :name="allGroupsExpanded ? 'minus' : 'plus'" :size="14" />
          </button>
        </template>
      </NgbRegisterGrid>
    </template>

    <template #drawerActions>
      <NgbEntityEditorDrawerActions
        :flags="editorFlags"
        restore-title="Restore"
        @action="handleDrawerAction"
      />
    </template>

    <template #drawerContent>
      <NgbChartOfAccountEditor
        ref="editorRef"
        v-if="isPanelOpen"
        :id="currentId"
        :metadata="metadata"
        :route-base-path="resolvedRouteBasePath"
        @state="handleEditorState"
        @flags="handleEditorFlags"
        @shell="handleEditorShell"
        @created="handleCreated"
        @saved="handleSaved"
        @changed="handleChanged"
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
