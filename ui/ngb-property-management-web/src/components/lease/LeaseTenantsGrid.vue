<script setup lang="ts">
import { computed, ref } from 'vue'
import { NgbIcon, NgbLookup, NgbSelect, buildLookupFieldTargetUrl, isReferenceValue, type ReferenceValue, useLookupStore, useValidationFocus } from 'ngb-ui-framework'
import { useRoute, useRouter } from 'vue-router'

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
const rows = computed(() => props.modelValue ?? [])
const lookupStore = useLookupStore()
const router = useRouter()
const route = useRoute()
const validationFocus = useValidationFocus(rootRef, { attribute: 'data-validation-cell' })

const roleOptions = [
  { value: 'PrimaryTenant', label: 'Primary tenant' },
  { value: 'CoTenant', label: 'Co-tenant' },
  { value: 'Occupant', label: 'Occupant' },
  { value: 'Guarantor', label: 'Guarantor' },
]

const canEdit = computed(() => !props.readonly)
const tenantErrors = computed(() => (props.errors?.summary ?? []).filter((x) => String(x ?? '').trim().length > 0))

function rowErrorMap(rowIndex: number): LeaseTenantRowErrors {
  return props.errors?.rowErrors?.[rowIndex] ?? {}
}

function rowFieldErrors(rowIndex: number, field: LeasePartyFieldKey): string[] {
  const values = rowErrorMap(rowIndex)[field] ?? []
  return values.filter((x) => String(x ?? '').trim().length > 0)
}

function firstRowFieldError(rowIndex: number, field: LeasePartyFieldKey): string | undefined {
  return rowFieldErrors(rowIndex, field)[0]
}

function rowHasErrors(rowIndex: number): boolean {
  const entry = rowErrorMap(rowIndex)
  return Object.values(entry).some((messages) => Array.isArray(messages) && messages.some((x) => String(x ?? '').trim().length > 0))
}

function cellKey(rowIndex: number, field: LeasePartyFieldKey): string {
  return `${rowIndex}:${field}`
}

function focusRowField(rowIndex: number, field: LeasePartyFieldKey): boolean {
  return validationFocus.focus(cellKey(rowIndex, field))
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
      const messages = entry.value?.[field] ?? []
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

async function onPartyQuery(rowIndex: number, q: string) {
  const query = (q ?? '').trim()
  if (query.length === 0) {
    lookupItemsByRow.value[rowIndex] = []
    return
  }

  const items = await lookupStore.searchCatalog('pm.party', query, { filters: { is_tenant: 'true' } })
  lookupItemsByRow.value[rowIndex] = (items ?? []).map((x) => ({
    id: x.id,
    label: x.label,
    meta: x.meta ?? undefined,
  }))
}

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
  if (!canEdit.value) return

  const next = rows.value.map((r) => ({ ...r }))

  const hasPrimary = next.some((r) => r.is_primary)
  next.push({
    party_id: null,
    role: 'CoTenant',
    is_primary: !hasPrimary,
    ordinal: next.length + 1,
  })

  // Ensure we always have a primary.
  if (!hasPrimary) {
    emit('update:modelValue', renumber(next))
    setPrimary(next.length - 1)
    return
  }

  emit('update:modelValue', renumber(next))
}

function removeRow(rowIndex: number) {
  if (!canEdit.value) return

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

/* ---------------- drag & drop ordering ---------------- */

const dragFrom = ref<number | null>(null)

function onDragStart(index: number, e: DragEvent) {
  if (!canEdit.value) return
  dragFrom.value = index
  try {
    e.dataTransfer?.setData('text/plain', String(index))
    e.dataTransfer?.setDragImage(new Image(), 0, 0)
  } catch {
    // ignore
  }
}

function onDragOver(_index: number, e: DragEvent) {
  if (!canEdit.value) return
  e.preventDefault()
}

function onDrop(index: number, e: DragEvent) {
  if (!canEdit.value) return
  e.preventDefault()

  const from = dragFrom.value ?? Number(e.dataTransfer?.getData('text/plain') ?? NaN)
  if (!Number.isFinite(from) || from === index) {
    dragFrom.value = null
    return
  }

  const next = rows.value.map((r) => ({ ...r }))
  const [moved] = next.splice(from, 1)
  next.splice(index, 0, moved)

  emit('update:modelValue', renumber(next))
  dragFrom.value = null
}
</script>

<template>
  <div ref="rootRef" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card overflow-hidden">
    <div class="px-4 py-3 border-b border-ngb-border">
      <div class="text-sm font-semibold text-ngb-text">Tenants</div>
      <div class="text-xs text-ngb-muted mt-0.5">At least one tenant is required. Exactly one must be Primary.</div>
      <ul v-if="tenantErrors.length" class="mt-2 space-y-1 text-xs text-ngb-danger list-disc pl-5">
        <li v-for="(msg, idx) in tenantErrors" :key="idx">{{ msg }}</li>
      </ul>
    </div>

    <table class="w-full text-sm table-fixed">
      <colgroup>
        <col style="width:28px" />
        <col style="width:44px" />
        <col />
        <col style="width:220px" />
        <col style="width:88px" />
        <col style="width:40px" />
      </colgroup>

      <thead class="bg-ngb-bg text-ngb-muted text-xs">
        <tr>
          <th class="px-2 py-2"></th>
          <th class="px-2 py-2 text-right font-semibold border-r border-dotted border-ngb-border">#</th>
          <th class="px-3 py-2 font-semibold truncate border-r border-dotted border-ngb-border">Party</th>
          <th class="px-3 py-2 font-semibold truncate border-r border-dotted border-ngb-border">Role</th>
          <th class="px-3 py-2 font-semibold text-center truncate border-r border-dotted border-ngb-border">Primary</th>
          <th class="px-2 py-2"></th>
        </tr>
      </thead>

      <tbody>
        <tr
          v-for="(r, i) in rows"
          :key="i"
          class="border-t border-ngb-border hover:bg-ngb-bg transition-colors align-top"
          :class="rowHasErrors(i) ? 'bg-red-50/40 dark:bg-red-950/10' : ''"
          :draggable="canEdit"
          @dragstart="onDragStart(i, $event)"
          @dragover="onDragOver(i, $event)"
          @drop="onDrop(i, $event)"
        >
          <td class="px-1 py-1 align-middle">
            <div
              class="h-8 w-6 flex items-center justify-center text-ngb-muted"
              :class="canEdit ? 'cursor-grab active:cursor-grabbing' : 'opacity-50'"
              :title="canEdit ? 'Drag to reorder' : ''"
            >
              <NgbIcon name="grip-vertical" :size="16" />
            </div>
          </td>

          <td class="px-2 py-1 align-top text-right text-ngb-muted border-r border-dotted border-ngb-border">
            <div :data-validation-cell="cellKey(i, 'ordinal')">
              <div>{{ i + 1 }}</div>
              <div v-if="firstRowFieldError(i, 'ordinal')" class="mt-1 text-left text-xs text-ngb-danger">
                {{ firstRowFieldError(i, 'ordinal') }}
              </div>
            </div>
          </td>

          <td class="px-0 py-1 align-top border-r border-dotted border-ngb-border">
            <div class="px-1" :data-validation-cell="cellKey(i, 'party_id')">
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
              <div v-if="firstRowFieldError(i, 'party_id')" class="mt-1 px-2 text-xs text-ngb-danger">
                {{ firstRowFieldError(i, 'party_id') }}
              </div>
            </div>
          </td>

          <td class="px-0 py-1 align-top border-r border-dotted border-ngb-border">
            <div class="px-1" :data-validation-cell="cellKey(i, 'role')">
              <NgbSelect
                :modelValue="r.role"
                :options="roleOptions"
                :disabled="!canEdit"
                variant="grid"
                @update:modelValue="(v) => setRole(i, String(v))"
              />
              <div v-if="firstRowFieldError(i, 'role')" class="mt-1 px-2 text-xs text-ngb-danger">
                {{ firstRowFieldError(i, 'role') }}
              </div>
            </div>
          </td>

          <td class="px-2 py-1 align-top text-center border-r border-dotted border-ngb-border">
            <div class="min-h-8" :data-validation-cell="cellKey(i, 'is_primary')">
              <div class="h-8 flex items-center justify-center">
                <input
                  type="checkbox"
                  class="h-4 w-4 rounded-[3px] border border-ngb-border bg-ngb-card"
                  :checked="!!r.is_primary"
                  :disabled="!canEdit"
                  @change="togglePrimary(i, ($event.target as HTMLInputElement).checked)"
                />
              </div>
              <div v-if="firstRowFieldError(i, 'is_primary')" class="mt-1 text-left text-xs text-ngb-danger">
                {{ firstRowFieldError(i, 'is_primary') }}
              </div>
            </div>
          </td>

          <td class="px-1 py-1 align-middle">
            <button
              type="button"
              class="h-8 w-8 rounded-[var(--ngb-radius)] flex items-center justify-center text-ngb-muted hover:text-ngb-text hover:bg-ngb-bg ngb-focus"
              title="Delete"
              :disabled="!canEdit"
              @click="removeRow(i)"
            >
              <NgbIcon name="trash" :size="16" />
            </button>
          </td>
        </tr>
      </tbody>
    </table>

    <div v-if="canEdit" class="px-3 py-2 border-t border-ngb-border flex justify-end">
      <button
        type="button"
        class="inline-flex items-center gap-2 h-9 px-3 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card text-sm text-ngb-text hover:bg-ngb-bg ngb-focus"
        @click="addRow"
      >
        <NgbIcon name="plus" :size="16" />
        <span>Add tenant</span>
      </button>
    </div>
  </div>
</template>
