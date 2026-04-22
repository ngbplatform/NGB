<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  buildCatalogFullPageUrl,
  NgbDrawer,
  NgbEditorDiscardDialog,
  NgbEntityEditorDrawerActions as EntityEditorDrawerActions,
  NgbIcon,
  NgbPageHeader,
  NgbRecycleBinFilter as RecycleBinFilter,
  NgbRegisterGrid,
  type CatalogItemDto,
  formatLooseEntityValue,
  getCatalogPage,
  normalizeTrashMode,
  type PageResponseDto,
  replaceCleanRouteQuery,
  type RecordFields,
  toErrorMessage,
  type EntityFormModel,
  type EntityHeaderIconAction,
  type QueryTrashMode as RecycleBinMode,
  type ReferenceValue,
  useEditorDrawerState,
  useEntityEditorCommitHandlers,
  useRouteQueryEditorDrawer,
} from 'ngb-ui-framework'
import { getPmBuildingSummary, type PmBuildingSummaryDto } from '../reporting/queries'
import type { PmEntityEditorHandle } from '../editor/types'
import PmEntityEditor from '../editor/pm/PmEntityEditor.vue'
import PmPropertyBulkCreateUnitsDialog from '../components/property/PmPropertyBulkCreateUnitsDialog.vue'
import { usePropertiesLegacyQueryCompat } from '../features/properties/usePropertiesLegacyQueryCompat'

const route = useRoute()
const router = useRouter()
usePropertiesLegacyQueryCompat(route, router)

const canBack = computed(() => route.path !== '/')

type PropertyGridFieldKey = 'address_line1' | 'city' | 'state' | 'zip' | 'unit_no' | 'parent_property_id'
type PropertyGridRow = Record<string, unknown> & {
  key: string
  display: unknown
  isDeleted?: boolean
  isMarkedForDeletion?: boolean
}

/* ---------------- Query helpers ---------------- */

function updateQuery(patch: Record<string, unknown>) {
  void replaceCleanRouteQuery(route, router, patch)
}

// Independent per-panel recycle-bin filters.
const buildingsTrashMode = computed<RecycleBinMode>({
  get() {
    return normalizeTrashMode(route.query.bTrash)
  },
  set(v) {
    updateQuery({ bTrash: v, bOffset: 0 })
  },
})

const unitsTrashMode = computed<RecycleBinMode>({
  get() {
    return normalizeTrashMode(route.query.uTrash)
  },
  set(v) {
    updateQuery({ uTrash: v, uOffset: 0 })
  },
})

const bOffset = computed(() => Number(route.query.bOffset ?? 0) || 0)
const bLimit = computed(() => Number(route.query.bLimit ?? 50) || 50)
const uOffset = computed(() => Number(route.query.uOffset ?? 0) || 0)
const uLimit = computed(() => Number(route.query.uLimit ?? 50) || 50)

const selectedBuildingId = computed(() => {
  const s = String(route.query.buildingId ?? '')
  return s ? s : null
})

const newKind = computed(() => String(route.query.newKind ?? ''))

function resolveDisplay(value: unknown): string | null {
  const normalized = String(value ?? '').trim()
  return normalized || null
}

/* ---------------- Data loading ---------------- */

const buildingsLoading = ref(false)
const unitsLoading = ref(false)
const error = ref<string | null>(null)

const buildingsPage = ref<PageResponseDto<CatalogItemDto> | null>(null)
const unitsPage = ref<PageResponseDto<CatalogItemDto> | null>(null)

const selectedBuildingDisplay = ref<string | null>(null)

const buildingSummaryLoading = ref(false)
const buildingSummaryError = ref<string | null>(null)
const buildingSummary = ref<PmBuildingSummaryDto | null>(null)

/* ---------------- Bulk create units (Buildings panel action) ---------------- */

const bulkUnitsOpen = ref(false)

function setBulkUnitsOpen(v: boolean) {
  bulkUnitsOpen.value = v
}

function openBulkCreateUnits() {
  if (!selectedBuildingId.value) return
  bulkUnitsOpen.value = true
}

async function handleBulkUnitsCreated() {
  // New units affect the Units grid and Building summary.
  await Promise.all([loadUnits(), loadBuildingSummary()])
}

function mapRow(it: CatalogItemDto, keys: readonly PropertyGridFieldKey[]): PropertyGridRow {
  const fields = (it.payload?.fields ?? {}) as RecordFields
  const row: PropertyGridRow = {
    key: it.id,
    isDeleted: it.isDeleted,
    isMarkedForDeletion: it.isMarkedForDeletion,
    display: it.display ?? fields.display ?? null,
  }

  // Prefer top-level display when present.
  for (const key of keys) row[key] = fields[key]
  return row
}

const buildingColumns = computed(() => [
  { key: 'display', title: 'Building', width: 280, pinned: 'left' as const, format: (value: unknown) => formatLooseEntityValue(value) },
  { key: 'address_line1', title: 'Address', width: 260, format: (value: unknown) => formatLooseEntityValue(value) },
  { key: 'city', title: 'City', width: 160, format: (value: unknown) => formatLooseEntityValue(value) },
  { key: 'state', title: 'State', width: 90, format: (value: unknown) => formatLooseEntityValue(value) },
  { key: 'zip', title: 'ZIP', width: 110, format: (value: unknown) => formatLooseEntityValue(value) },
])

const unitColumns = computed(() => [
  { key: 'display', title: 'Unit', width: 320, pinned: 'left' as const, format: (value: unknown) => formatLooseEntityValue(value) },
  { key: 'unit_no', title: 'Unit No', width: 140, format: (value: unknown) => formatLooseEntityValue(value) },
])

const buildingRows = computed(() => {
  const items = buildingsPage.value?.items ?? []
  return items.map((it) => mapRow(it, ['address_line1', 'city', 'state', 'zip']))
})

const unitRows = computed(() => {
  const items = unitsPage.value?.items ?? []
  return items.map((it) => mapRow(it, ['unit_no', 'parent_property_id']))
})

async function loadBuildings() {
  buildingsLoading.value = true
  error.value = null
  try {
    buildingsPage.value = await getCatalogPage('pm.property', {
      offset: bOffset.value,
      limit: bLimit.value,
      filters: {
        deleted: buildingsTrashMode.value,
        kind: 'Building',
      },
    })

    // Refresh selected building display when it is present on the current page.
    if (selectedBuildingId.value) {
      const hit = buildingsPage.value.items.find((item) => item.id === selectedBuildingId.value)
      if (hit) selectedBuildingDisplay.value = resolveDisplay(hit.display ?? hit.payload?.fields?.display)
    }
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to load buildings.')
  } finally {
    buildingsLoading.value = false
  }
}

async function refreshBuildingsPanel() {
  await Promise.all([loadBuildings(), loadBuildingSummary()])
}

async function loadUnits() {
  if (!selectedBuildingId.value) {
    unitsPage.value = { items: [], offset: 0, limit: uLimit.value, total: 0 }
    return
  }

  unitsLoading.value = true
  error.value = null
  try {
    unitsPage.value = await getCatalogPage('pm.property', {
      offset: uOffset.value,
      limit: uLimit.value,
      filters: {
        deleted: unitsTrashMode.value,
        kind: 'Unit',
        parent_property_id: selectedBuildingId.value,
      },
    })
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to load units.')
  } finally {
    unitsLoading.value = false
  }
}

async function refreshUnitsPanel() {
  await Promise.all([loadUnits(), loadBuildingSummary()])
}

function formatPercent(v: number): string {
  if (!Number.isFinite(v)) return '—'
  const rounded = Math.round(v * 100) / 100
  const isInt = Math.abs(rounded - Math.round(rounded)) < 1e-9
  return isInt ? `${Math.round(rounded)}%` : `${rounded.toFixed(2)}%`
}

async function loadBuildingSummary() {
  if (!selectedBuildingId.value) {
    buildingSummary.value = null
    buildingSummaryError.value = null
    return
  }

  buildingSummaryLoading.value = true
  buildingSummaryError.value = null

  try {
    const s = await getPmBuildingSummary(selectedBuildingId.value)
    buildingSummary.value = s
    // Prefer report display (server-side computed) when available.
    if (s.buildingDisplay) selectedBuildingDisplay.value = s.buildingDisplay
  } catch (cause) {
    buildingSummary.value = null
    buildingSummaryError.value = toErrorMessage(cause, 'Failed to load the building summary.')
  } finally {
    buildingSummaryLoading.value = false
  }
}

watch(
  () => [buildingsTrashMode.value, bOffset.value, bLimit.value],
  () => void loadBuildings(),
  { immediate: true },
)

watch(
  () => [unitsTrashMode.value, selectedBuildingId.value, uOffset.value, uLimit.value],
  () => void loadUnits(),
  { immediate: true },
)

watch(
  () => selectedBuildingId.value,
  () => void loadBuildingSummary(),
  { immediate: true },
)

/* ---------------- Selection ---------------- */

function onBuildingSelected(keys: string[]) {
  const id = keys?.[0] ? String(keys[0]) : null
  if (!id) {
    selectedBuildingDisplay.value = null
    clearUnitSelection()
    updateQuery({ buildingId: null, uOffset: 0 })
    return
  }

  // Try to capture display immediately from current page.
  const hit = buildingsPage.value?.items?.find((item) => item.id === id)
  selectedBuildingDisplay.value = resolveDisplay(hit?.display ?? hit?.payload?.fields?.display) ?? selectedBuildingDisplay.value

  clearUnitSelection()
  updateQuery({ buildingId: id, uOffset: 0 })
}

const selectedBuildingKeys = computed(() => (selectedBuildingId.value ? [selectedBuildingId.value] : []))

const selectedUnitKeys = ref<string[]>([])

function clearUnitSelection() {
  selectedUnitKeys.value = []
}

/* ---------------- Paging ---------------- */

function buildingsNext() {
  updateQuery({ bOffset: bOffset.value + bLimit.value })
}
function buildingsPrev() {
  updateQuery({ bOffset: Math.max(0, bOffset.value - bLimit.value) })
}
function unitsNext() {
  updateQuery({ uOffset: uOffset.value + uLimit.value })
}
function unitsPrev() {
  updateQuery({ uOffset: Math.max(0, uOffset.value - uLimit.value) })
}

/* ---------------- Drawer (editor) ---------------- */

const editorRef = ref<PmEntityEditorHandle | null>(null)
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
  clearKeys: ['newKind'],
  onBeforeOpen: async (_next, current) => {
    if (current.mode === null || !editorFlags.value.isDirty) return true
    return await requestDiscard()
  },
})

async function closeDrawer() {
  await closeRouteDrawer()
}

async function reloadRegisters() {
  await Promise.all([loadBuildings(), loadUnits(), loadBuildingSummary()])
}

function openCreateBuilding() {
  void openRouteCreateDrawer({
    patch: { newKind: 'Building' },
    onCommit: () => {
      resetDrawerHeading()
    },
  })
}

function openCreateUnit() {
  if (!selectedBuildingId.value) return
  void openRouteCreateDrawer({
    patch: { newKind: 'Unit' },
    onCommit: () => {
      resetDrawerHeading()
    },
  })
}

function openEdit(id: string) {
  if (!id) return
  void openRouteEditDrawer(id, {
    onCommit: () => {
      resetDrawerHeading()
    },
  })
}

function openEditSelectedBuilding() {
  if (!selectedBuildingId.value) return
  openEdit(selectedBuildingId.value)
}

function openEditSelectedUnit() {
  const id = selectedUnitKeys.value?.[0]
  if (!id) return
  openEdit(id)
}

const selectedBuildingRef = computed<ReferenceValue | null>(() => {
  if (!selectedBuildingId.value) return null
  return {
    id: selectedBuildingId.value,
    display: selectedBuildingDisplay.value ?? 'Building',
  }
})

const initialFields = computed<EntityFormModel | null>(() => {
  if (panelMode.value !== 'new') return null
  if (newKind.value === 'Building') return { kind: 'Building' }
  if (newKind.value === 'Unit') {
    if (!selectedBuildingRef.value) return { kind: 'Unit' }
    return { kind: 'Unit', parent_property_id: selectedBuildingRef.value }
  }
  return null
})

const expandTo = computed(() => {
  // Keep full-page routes functional for power users.
  if (panelMode.value === 'new') return buildCatalogFullPageUrl('pm.property')
  if (panelMode.value === 'edit' && currentId.value) return buildCatalogFullPageUrl('pm.property', currentId.value)
  return null
})

const drawerExtraActions = computed<EntityHeaderIconAction[]>(() =>
  editorFlags.value.extras?.bulkCreateUnits
    ? [{ key: 'bulkCreateUnits', title: 'Bulk create units', icon: 'grid', disabled: editorFlags.value.loading || editorFlags.value.saving }]
    : [],
)

function handleDrawerAction(action: string) {
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
    case 'bulkCreateUnits':
      editorRef.value?.openBulkCreateUnitsWizard()
      return
    case 'save':
      void editorRef.value?.save()
  }
}

const {
  handleCreated,
  handleSaved,
  handleChanged,
  handleDeleted,
} = useEntityEditorCommitHandlers({
  reload: reloadRegisters,
  closeDrawer,
  onCreated: async ({ id }) => {
    if (newKind.value === 'Building') {
      updateQuery({ buildingId: id, uOffset: 0, bOffset: 0 })
    }
  },
})

const buildingsDisableNext = computed(() => (buildingsPage.value?.items?.length ?? 0) < bLimit.value)
const buildingsDisablePrev = computed(() => bOffset.value === 0)
const unitsDisableNext = computed(() => (unitsPage.value?.items?.length ?? 0) < uLimit.value)
const unitsDisablePrev = computed(() => uOffset.value === 0)

const unitsSubtitle = computed(() => {
  if (!selectedBuildingId.value) return 'Select a building to see units'
  return selectedBuildingDisplay.value ?? 'Units'
})

const headerSubtitle = computed(() => {
  const p = buildingsPage.value
  if (!p) return '\u00A0'
  const left = String(p.items?.length ?? 0)
  const right = p.total != null ? ' / ' + String(p.total) : ''
  return left + right
})

const propertySummaryCards = computed(() => {
  const totalFallback = unitsPage.value
    ? (unitsPage.value.total != null ? unitsPage.value.total : unitsPage.value.items?.length ?? 0)
    : null

  if (buildingSummary.value) {
    return [
      { label: 'Total', value: String(buildingSummary.value.totalUnits) },
      { label: 'Occupied', value: String(buildingSummary.value.occupiedUnits) },
      { label: 'Vacant', value: String(buildingSummary.value.vacantUnits) },
      { label: 'Vacancy', value: formatPercent(buildingSummary.value.vacancyPercent) },
    ]
  }

  return [
    { label: 'Total', value: totalFallback != null ? String(totalFallback) : '—' },
    { label: 'Occupied', value: buildingSummaryLoading.value ? '…' : 'N/A' },
    { label: 'Vacant', value: buildingSummaryLoading.value ? '…' : 'N/A' },
    { label: 'Vacancy', value: buildingSummaryLoading.value ? '…' : 'N/A' },
  ]
})

</script>

<template>
  <div data-testid="properties-page" class="h-full min-h-0 flex flex-col">
    <NgbPageHeader title="Properties" :can-back="canBack" @back="router.back()">
      <template #secondary>
        <div v-if="headerSubtitle" class="text-xs text-ngb-muted truncate">{{ headerSubtitle }}</div>
      </template>
      <template #actions>
        <!--
          Building summary (tiles only, as per screenshot).
          NgbPageHeader computes its height from the tallest child in the header row; these tiles
          are taller than the title line, so the header "jumps" when tiles appear.
          Keep the tile design, but make the slot wrapper height-neutral (h-0) and render tiles
          via overflow so they don't affect header height.
        -->
        <div v-if="selectedBuildingId" class="hidden lg:block h-0 overflow-visible shrink-0" :title="buildingSummaryError || ''">
          <div class="flex items-center justify-end gap-2 tabular-nums -translate-y-1/2" :class="buildingSummaryLoading ? 'opacity-70' : ''">
            <div
              v-for="card in propertySummaryCards"
              :key="card.label"
              class="px-3 py-1.5 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-surface leading-tight text-center min-w-[64px]"
            >
              <div class="text-xs text-ngb-muted text-center">{{ card.label }}</div>
              <div class="text-sm font-semibold text-center">{{ card.value }}</div>
            </div>
          </div>
        </div>
      </template>
    </NgbPageHeader>

    <div class="p-6 flex-1 min-h-0 flex flex-col">
      <div
        v-if="error"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div data-testid="properties-panels" class="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Buildings -->
        <NgbRegisterGrid
          data-testid="properties-buildings-panel"
          class="min-h-0"
          fill-height
          :show-panel="true"
          title="Buildings"
          :subtitle="buildingsPage ? `${buildingsPage.items.length}${buildingsPage.total != null ? ' / ' + buildingsPage.total : ''}` : ''"
          :columns="buildingColumns"
          :rows="buildingRows"
          :show-totals="false"
          :storage-key="'pm:properties:buildings'"
          :selected-keys="selectedBuildingKeys"
          @update:selectedKeys="onBuildingSelected"
          @rowActivate="(id) => openEdit(String(id))"
        >
          <template #toolbar>
            <div class="flex items-center gap-1.5">
              <RecycleBinFilter v-model="buildingsTrashMode" :disabled="buildingsLoading" />
              <div class="w-px h-5 bg-ngb-border mx-1" />
              <button class="ngb-iconbtn" title="Create building" :disabled="buildingsLoading" @click="openCreateBuilding">
                <NgbIcon name="plus" />
              </button>
              <button class="ngb-iconbtn" title="Edit" :disabled="!selectedBuildingId || buildingsLoading" @click="openEditSelectedBuilding">
                <NgbIcon name="edit" />
              </button>
              <button
                class="ngb-iconbtn"
                title="Bulk create units"
                :disabled="!selectedBuildingId || buildingsLoading"
                @click="openBulkCreateUnits"
              >
                <NgbIcon name="grid" />
              </button>
              <button class="ngb-iconbtn" title="Refresh" :disabled="buildingsLoading" @click="refreshBuildingsPanel">
                <NgbIcon name="refresh" />
              </button>
              <button class="ngb-iconbtn" title="Previous" :disabled="buildingsDisablePrev || buildingsLoading" @click="buildingsPrev">
                <NgbIcon name="arrow-left" />
              </button>
              <button class="ngb-iconbtn" title="Next" :disabled="buildingsDisableNext || buildingsLoading" @click="buildingsNext">
                <NgbIcon name="arrow-right" />
              </button>
            </div>
          </template>
        </NgbRegisterGrid>

        <!-- Units -->
        <NgbRegisterGrid
          data-testid="properties-units-panel"
          class="min-h-0"
          fill-height
          :show-panel="true"
          title="Units"
          :subtitle="unitsSubtitle"
          :columns="unitColumns"
          :rows="unitRows"
          :show-totals="false"
          :storage-key="'pm:properties:units'"
          v-model:selectedKeys="selectedUnitKeys"
          :activate-on-row-click="false"
          @rowActivate="(id) => openEdit(String(id))"
        >
          <template #toolbar>
            <div class="flex items-center gap-1.5">
              <RecycleBinFilter v-model="unitsTrashMode" :disabled="unitsLoading" />
              <div class="w-px h-5 bg-ngb-border mx-1" />
              <button class="ngb-iconbtn" title="Create unit" :disabled="!selectedBuildingId || unitsLoading" @click="openCreateUnit">
                <NgbIcon name="plus" />
              </button>
              <button class="ngb-iconbtn" title="Edit" :disabled="!selectedUnitKeys.length || unitsLoading" @click="openEditSelectedUnit">
                <NgbIcon name="edit" />
              </button>
              <button class="ngb-iconbtn" title="Refresh" :disabled="!selectedBuildingId || unitsLoading" @click="refreshUnitsPanel">
                <NgbIcon name="refresh" />
              </button>
              <button class="ngb-iconbtn" title="Previous" :disabled="!selectedBuildingId || unitsDisablePrev || unitsLoading" @click="unitsPrev">
                <NgbIcon name="arrow-left" />
              </button>
              <button class="ngb-iconbtn" title="Next" :disabled="!selectedBuildingId || unitsDisableNext || unitsLoading" @click="unitsNext">
                <NgbIcon name="arrow-right" />
              </button>
              <div class="w-px h-5 bg-ngb-border mx-1" />
              <button class="ngb-iconbtn" title="Clear selection" :disabled="!selectedBuildingId" @click="onBuildingSelected([])">
                <NgbIcon name="x" />
              </button>
            </div>
          </template>
        </NgbRegisterGrid>
      </div>

      <div v-if="buildingsLoading || unitsLoading" class="mt-3 text-sm text-ngb-muted">Loading…</div>
    </div>

    <NgbDrawer
      :open="isPanelOpen"
      :title="drawerTitle || (panelMode === 'new' ? (newKind === 'Unit' ? 'New Unit' : 'New Building') : 'Edit')"
      :subtitle="drawerSubtitle"
      :before-close="beforeCloseDrawer"
      @update:open="(v) => (!v ? void closeDrawer() : null)"
    >
      <template #actions>
        <EntityEditorDrawerActions
          :flags="editorFlags"
          :extra-actions="drawerExtraActions"
          @action="handleDrawerAction"
        />
      </template>

      <PmEntityEditor
        ref="editorRef"
        v-if="isPanelOpen"
        kind="catalog"
        type-code="pm.property"
        :id="currentId"
        mode="drawer"
        :initial-fields="initialFields"
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
    </NgbDrawer>

    <NgbEditorDiscardDialog
      :open="discardOpen"
      @cancel="discardCancel"
      @confirm="discardConfirm"
    />

    <PmPropertyBulkCreateUnitsDialog
      v-if="selectedBuildingId"
      :open="bulkUnitsOpen"
      :building-id="selectedBuildingId"
      :building-display="selectedBuildingDisplay"
      @update:open="setBulkUnitsOpen"
      @created="handleBulkUnitsCreated"
    />
  </div>
</template>
