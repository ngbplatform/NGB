<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { NgbLookup, NgbSelect, buildLookupFieldTargetUrl, isReferenceValue, type ReferenceValue, useLookupStore } from '@ngbplatform/ui'
import { useRoute, useRouter } from 'vue-router'
import { NgbDocumentLines, type DocumentLinesHandle } from '@ngbplatform/ui/editor'

type LookupItem = { id: string; label: string; meta?: string }

export type LeasePartyFieldKey = 'party_id' | 'role' | 'is_primary' | 'ordinal'
export type LeasePartyReference = ReferenceValue | string

export type LeasePartyRow = {
  party_id: LeasePartyReference | null
  role: string
  is_primary: boolean
  ordinal: number
}

export type LeaseTenantRowErrors = Partial<Record<LeasePartyFieldKey, string[]>>

export type LeaseTenantValidation = {
  summary?: string[] | null
  rowErrors?: Record<number, LeaseTenantRowErrors> | null
  focusTarget?: { rowIndex: number; field: LeasePartyFieldKey } | null
}

const props = withDefaults(
  defineProps<{
    modelValue: LeasePartyRow[]
    readonly?: boolean
    errors?: LeaseTenantValidation | null
  }>(),
  {
    readonly: false,
    errors: null,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', v: LeasePartyRow[]): void
}>()

const rootRef = ref<HTMLElement | null>(null)
const linesRef = ref<DocumentLinesHandle | null>(null)
const rows = computed(() => props.modelValue)
const lookupStore = useLookupStore()
const router = useRouter()
const route = useRoute()

const roleOptions = [
  { value: 'PrimaryTenant', label: 'Primary tenant' },
  { value: 'CoTenant', label: 'Co-tenant' },
  { value: 'Occupant', label: 'Occupant' },
  { value: 'Guarantor', label: 'Guarantor' },
]

const canEdit = computed(() => !props.readonly)
const tenantErrors = computed(() => (props.errors?.summary ?? []).filter((x) => String(x).trim().length > 0))

function focusRowField(rowIndex: number, field: LeasePartyFieldKey): boolean {
  return linesRef.value?.focusCell(rowIndex, field) ?? false
}

function focusFirstError(validation?: LeaseTenantValidation | null): boolean {
  const current = validation ?? props.errors
  if (!current) return false

  const preferred = current.focusTarget
  if (preferred && focusRowField(preferred.rowIndex, preferred.field)) return true

  const rowEntries = Object.entries(current.rowErrors ?? {})
    .map(([key, value]) => ({ rowIndex: Number(key), value }))
    .filter((x) => Number.isFinite(x.rowIndex))
    .sort((a, b) => a.rowIndex - b.rowIndex)

  for (const entry of rowEntries) {
    for (const field of ['party_id', 'role', 'is_primary', 'ordinal'] as LeasePartyFieldKey[]) {
      const messages = entry.value[field] ?? []
      if (messages.length > 0 && focusRowField(entry.rowIndex, field)) return true
    }
  }

  if (tenantErrors.value.length > 0) {
    rootRef.value?.scrollIntoView({ block: 'center', behavior: 'smooth' })
    return true
  }

  return false
}

defineExpose({
  focusRowField,
  focusFirstError,
})

// Per-row lookup cache (items shown in the dropdown)
const lookupItemsByRow = ref<Record<number, LookupItem[]>>({})
const lookupControllers = new Map<number, AbortController>()

async function onPartyQuery(rowIndex: number, q: string) {
  lookupControllers.get(rowIndex)?.abort()
  const query = (q ?? '').trim()
  if (query.length === 0) {
    lookupItemsByRow.value[rowIndex] = []
    return
  }

  const controller = new AbortController()
  lookupControllers.set(rowIndex, controller)
  try {
    const items = await lookupStore.searchCatalog('pm.party', query, {
      filters: { is_tenant: 'true' },
      signal: controller.signal,
    })
    if (lookupControllers.get(rowIndex) !== controller) return
    lookupItemsByRow.value[rowIndex] = (items ?? []).map((x) => ({ id: x.id, label: x.label, meta: x.meta ?? undefined }))
  } catch (error) {
    if (lookupControllers.get(rowIndex) !== controller) return
    throw error
  } finally {
    if (lookupControllers.get(rowIndex) === controller) lookupControllers.delete(rowIndex)
  }
}

onBeforeUnmount(() => {
  lookupControllers.forEach((controller) => controller.abort())
  lookupControllers.clear()
})

function toLookupValue(v: LeasePartyRow['party_id'] | null): LookupItem | null {
  if (!v) return null
  if (typeof v === 'string') return { id: v, label: v }
  if (isReferenceValue(v)) return { id: v.id, label: v.display || v.id }
  return null
}

function renumber(list: LeasePartyRow[]): LeasePartyRow[] {
  // Keep ordinals stable, 1..N. This avoids DB unique violations and keeps UX predictable.
  return list.map((r, i) => ({ ...r, ordinal: i + 1 }))
}

function setRow(rowIndex: number, patch: Partial<LeasePartyRow>) {
  const next = rows.value.map((r) => ({ ...r }))
  next[rowIndex] = { ...next[rowIndex], ...patch }
  emit('update:modelValue', renumber(next))
}

function setPrimary(rowIndex: number) {
  const next = rows.value.map((r, i) => ({ ...r, is_primary: i === rowIndex }))

  // UX: primary row must be PrimaryTenant.
  for (let i = 0; i < next.length; i++) {
    if (i === rowIndex) {
      next[i].role = 'PrimaryTenant'
      continue
    }

    if (next[i].role === 'PrimaryTenant') next[i].role = 'CoTenant'
  }

  emit('update:modelValue', renumber(next))
}

function togglePrimary(rowIndex: number, checked: boolean) {
  // Invariant UX: exactly one primary. Can't uncheck the current primary.
  if (!checked && rows.value[rowIndex]?.is_primary) return
  if (checked) setPrimary(rowIndex)
}

function setRole(rowIndex: number, role: string) {
  // If user sets PrimaryTenant role, also set as primary.
  if (role === 'PrimaryTenant') {
    setPrimary(rowIndex)
    return
  }

  setRow(rowIndex, { role })
}

function onPartySelect(rowIndex: number, item: LookupItem | null) {
  if (!item) {
    setRow(rowIndex, { party_id: null })
    return
  }

  setRow(rowIndex, { party_id: { id: item.id, display: item.label } })
}

async function openParty(rowIndex: number) {
  const target = await buildLookupFieldTargetUrl({
    hint: { kind: 'catalog', catalogType: 'pm.party' },
    value: rows.value[rowIndex]?.party_id ?? null,
    route,
  })

  if (!target) return
  await router.push(target)
}

function addRow() {
  const next = rows.value.map((r) => ({ ...r }))

  const hasPrimary = next.some((r) => r.is_primary)
  next.push({
    party_id: null,
    role: hasPrimary ? 'CoTenant' : 'PrimaryTenant',
    is_primary: !hasPrimary,
    ordinal: next.length + 1,
  })

  emit('update:modelValue', renumber(next))
}

function removeRow(rowIndex: number) {
  const next = rows.value.map((r) => ({ ...r }))
  const removedWasPrimary = !!next[rowIndex]?.is_primary

  next.splice(rowIndex, 1)

  // Keep at least one row (lease requires tenants).
  if (next.length === 0) {
    emit('update:modelValue', [
      {
        party_id: null,
        role: 'PrimaryTenant',
        is_primary: true,
        ordinal: 1,
      },
    ])
    return
  }

  if (removedWasPrimary) {
    // Pick first row as new primary.
    for (let i = 0; i < next.length; i++) next[i].is_primary = false
    next[0].is_primary = true
    next[0].role = 'PrimaryTenant'
  }

  emit('update:modelValue', renumber(next))
}

const columns = [
  { key: 'party_id', label: 'Party' },
  { key: 'role', label: 'Role', width: '220px' },
  { key: 'is_primary', label: 'Primary', width: '88px', align: 'center' as const },
]

function reorderRows(fromIndex: number, toIndex: number): void {
  if (!canEdit.value) return
  const next = rows.value.map((row) => ({ ...row }))
  const [moved] = next.splice(fromIndex, 1)
  if (!moved) return
  next.splice(toIndex, 0, moved)
  emit('update:modelValue', renumber(next))
}
</script>

<template>
  <div ref="rootRef">
    <NgbDocumentLines
      ref="linesRef"
      title="Tenants"
      description="At least one tenant is required. Exactly one must be Primary."
      :rows="rows"
      :columns="columns"
      :readonly="readonly"
      readonly-actions="disable"
      add-label="Add tenant"
      :errors="errors?.rowErrors"
      :summary-errors="tenantErrors"
      @add="addRow"
      @remove="removeRow"
      @reorder="reorderRows"
    >
      <template #cell="{ row: r, rowIndex: i, column }">
        <div v-if="column.key === 'party_id'" class="px-1">
          <NgbLookup
            :modelValue="toLookupValue(r.party_id)"
            :items="lookupItemsByRow[i] ?? []"
            placeholder="Search tenant…"
            :readonly="!canEdit"
            :show-open="!!toLookupValue(r.party_id)"
            :show-clear="canEdit && !!toLookupValue(r.party_id)"
            variant="grid"
            @query="(q) => onPartyQuery(i, q)"
            @update:modelValue="(v) => onPartySelect(i, v)"
            @open="void openParty(i)"
          />
        </div>

        <div v-if="column.key === 'role'" class="px-1">
          <NgbSelect
            :modelValue="r.role"
            :options="roleOptions"
            :disabled="!canEdit"
            variant="grid"
            @update:modelValue="(v) => setRole(i, String(v))"
          />
        </div>

        <div v-if="column.key === 'is_primary'" class="min-h-8">
          <div class="h-8 flex items-center justify-center">
            <input
              type="checkbox"
              class="h-4 w-4 rounded-[3px] border border-ngb-border bg-ngb-card"
              :checked="!!r.is_primary"
              :disabled="!canEdit"
              @change="togglePrimary(i, ($event.target as HTMLInputElement).checked)"
            />
          </div>
        </div>
      </template>
    </NgbDocumentLines>
  </div>
</template>
