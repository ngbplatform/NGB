<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  NgbButton,
  NgbDrawer,
  NgbEntityAuditSidebar as EntityAuditSidebar,
  NgbEntityForm as EntityForm,
  NgbIcon,
  NgbPageHeader,
  buildFieldsPayload,
  type CatalogItemDto,
  type CatalogTypeMetadataDto,
  clonePlainData,
  copyAppLink,
  ensureModelKeys,
  type FormMetadataDto,
  getCatalogPage,
  httpPost,
  stableStringify,
  toErrorMessage,
  updateCatalog,
  type EntityFormModel,
  useMetadataStore,
  useToasts,
} from 'ngb-ui-framework'

const router = useRouter()
const toasts = useToasts()
const metaStore = useMetadataStore()

const catalogType = 'pm.accounting_policy'

const loading = ref(false)
const saving = ref(false)
const initializing = ref(false)
const error = ref<string | null>(null)

const fullMeta = ref<CatalogTypeMetadataDto | null>(null)
const uiForm = ref<FormMetadataDto | null>(null)

const policy = ref<CatalogItemDto | null>(null)
const model = ref<EntityFormModel>({})
const auditOpen = ref(false)

const auditEntityKind = 2
const auditEntityId = computed(() => policy.value?.id ?? null)
const auditEntityTitle = computed(() => String(model.value.display ?? policy.value?.display ?? 'Accounting Policy').trim() || 'Accounting Policy')

const initialSnapshot = ref('')
const isDirty = computed(() => initialSnapshot.value !== '' && initialSnapshot.value !== stableStringify(model.value))
const hasPolicy = computed(() => !!policy.value)

function cloneForm(f: FormMetadataDto): FormMetadataDto {
  return clonePlainData(f)
}

function buildUiFormFrom(full: FormMetadataDto): FormMetadataDto {
  const out = cloneForm(full)

  const hiddenKeys = new Set<string>([
    // Not user-facing in the UI.
    'display',
    // System-managed OR references (should not be editable from UI).
    'tenant_balances_register_id',
    'receivables_open_items_register_id',
    'payables_open_items_register_id',
  ])

  for (const s of out.sections ?? []) {
    for (const r of s.rows ?? []) {
      r.fields = (r.fields ?? [])
        .filter((f) => !hiddenKeys.has(f.key))
        .map((f) => {
          if (f.key === 'cash_account_id') {
            f.label = 'Default Cash Control Account'
            f.helpText = 'Fallback GL cash account. Individual bank accounts may override this in payment documents.'
          }

          if (f.key === 'ar_tenants_account_id') {
            f.label = 'Tenant Receivables (A/R) Account'
            f.helpText = 'Debited when posting tenant charges (e.g., rent) to the General Ledger.'
          }

          if (f.key === 'ap_vendors_account_id') {
            f.label = 'Vendor Payables (A/P) Account'
            f.helpText = 'Credited when posting payable vendor charges to the General Ledger.'
          }

          if (f.key === 'rent_income_account_id') {
            f.label = 'Rental Income Account'
            f.helpText = 'Credited when posting tenant charges (rent) to the General Ledger.'
          }

          if (f.key === 'late_fee_income_account_id') {
            f.label = 'Late Fee Income Account'
            f.helpText = 'Credited when posting late fee charges to the General Ledger.'
          }

          return f
        })
    }
  }

  // Drop empty rows/sections after filtering.
  out.sections = (out.sections ?? [])
    .map((s) => ({
      ...s,
      rows: (s.rows ?? []).filter((r) => (r.fields ?? []).length > 0),
    }))
    .filter((s) => (s.rows ?? []).length > 0)

  // Rename the default section title to feel like Settings, not a technical "Main".
  if (out.sections.length === 1 && (out.sections[0].title ?? '').toLowerCase() === 'main') {
    out.sections[0].title = 'Settings'
  }

  return out
}

function ensureDisplayValue() {
  const v = String(model.value.display ?? '').trim()
  if (!v) model.value.display = 'Accounting Policy'
}

async function copyShareLink(): Promise<void> {
  if (!policy.value?.id) return
  await copyAppLink(router, toasts, { path: '/catalogs/pm.accounting_policy' })
}

function openAuditLog(): void {
  if (!auditEntityId.value) return
  auditOpen.value = true
}

function closeAuditLog(): void {
  auditOpen.value = false
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null

  try {
    fullMeta.value = await metaStore.ensureCatalogType(catalogType)
    uiForm.value = fullMeta.value.form ? buildUiFormFrom(fullMeta.value.form) : null

    const page = await getCatalogPage(catalogType, {
      offset: 0,
      limit: 1,
      filters: { deleted: 'active' },
    })

    policy.value = page.items?.[0] ?? null
    model.value = { ...((policy.value?.payload?.fields ?? {}) as EntityFormModel) }

    // Ensure model has all keys (including hidden/system keys) so that Save preserves them.
    if (fullMeta.value.form) ensureModelKeys(fullMeta.value.form, model.value)
    ensureDisplayValue()

    initialSnapshot.value = stableStringify(model.value)
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to load the accounting policy.')
  } finally {
    loading.value = false
  }
}

async function save(): Promise<void> {
  if (!policy.value) return
  if (!fullMeta.value?.form) return
  if (saving.value) return

  saving.value = true
  error.value = null
  try {
    ensureDisplayValue()

    await updateCatalog(catalogType, policy.value.id, {
      fields: buildFieldsPayload(fullMeta.value.form, model.value),
    })

    toasts.push({
      title: 'Saved',
      message: 'Accounting policy updated.',
      tone: 'success',
    })
    await load()
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to save the accounting policy.')
  } finally {
    saving.value = false
  }
}

async function applyDefaults(): Promise<void> {
  if (initializing.value) return

  initializing.value = true
  error.value = null
  try {
    await httpPost<void>('/api/admin/setup/apply-defaults')
    toasts.push({
      title: 'Defaults applied',
      message: 'Default configuration has been created/updated.',
      tone: 'success',
    })
    await load()
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to apply default configuration.')
  } finally {
    initializing.value = false
  }
}

watch(
  () => catalogType,
  () => {
    void load()
  },
  { immediate: true },
)

const canSave = computed(() => hasPolicy.value && !loading.value && !saving.value && !!uiForm.value)
</script>

<template>
  <div class="h-full min-h-0 flex flex-col" data-testid="accounting-policy-page">
    <NgbPageHeader
      title="Accounting Policy"
      can-back
      @back="router.back()"
    >
      <template #secondary>
        <div class="text-xs text-ngb-muted truncate">Settings</div>
      </template>
      <template #actions>
        <div class="flex items-center gap-1.5">
          <button
            v-if="hasPolicy"
            class="ngb-iconbtn"
            :disabled="!auditEntityId || loading || saving || initializing"
            title="Share link"
            @click="copyShareLink"
          >
            <NgbIcon name="share" />
          </button>

          <button
            v-if="hasPolicy"
            class="ngb-iconbtn"
            :disabled="!auditEntityId || loading || saving || initializing"
            title="Audit log"
            @click="openAuditLog"
          >
            <NgbIcon name="history" />
          </button>

          <button
            class="ngb-iconbtn"
            :disabled="!canSave || !isDirty"
            title="Save"
            @click="save"
          >
            <NgbIcon name="save" />
          </button>

          <button
            class="ngb-iconbtn"
            :disabled="loading || saving || initializing"
            title="Refresh"
            @click="load"
          >
            <NgbIcon name="refresh" />
          </button>
        </div>
      </template>
    </NgbPageHeader>

    <div class="p-6 flex-1 min-h-0 overflow-auto" data-testid="accounting-policy-scroll-region">
      <div
        v-if="error"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/30 p-3 text-sm text-red-900 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div v-if="loading" class="text-sm text-ngb-muted">Loading…</div>

      <template v-else>
        <!-- Policy is intended to be single-record and is normally created by PM Setup -->
        <div
          v-if="!hasPolicy"
          data-testid="accounting-policy-empty-state"
          class="rounded-[var(--ngb-radius)] border border-ngb-border bg-white dark:bg-ngb-panel p-4"
        >
          <div class="text-sm font-semibold">Not initialized</div>
          <div class="text-sm text-ngb-muted mt-1">
            Accounting Policy is created by the Property Management setup workflow. Apply defaults to create the policy,
            create required Chart of Accounts accounts, and configure Operational Registers.
          </div>

          <div class="mt-3">
            <NgbButton :disabled="initializing" @click="applyDefaults">
              <span class="inline-flex items-center gap-2">
                <NgbIcon name="check" />
                Apply defaults
              </span>
            </NgbButton>
          </div>
        </div>

        <div v-else class="space-y-4">
          <div v-if="uiForm" data-testid="accounting-policy-form">
            <EntityForm
              :form="uiForm"
              :model="model"
              :entity-type-code="catalogType"
            />
          </div>
          <div v-else class="text-sm text-ngb-muted">No form metadata available.</div>
        </div>
      </template>
    </div>

    <NgbDrawer
      v-model:open="auditOpen"
      title="Audit Log"
      hide-header
      flush-body
    >
      <EntityAuditSidebar
        :open="auditOpen"
        :entity-kind="auditEntityKind"
        :entity-id="auditEntityId"
        :entity-title="auditEntityTitle"
        @back="closeAuditLog"
        @close="closeAuditLog"
      />
    </NgbDrawer>
  </div>
</template>
