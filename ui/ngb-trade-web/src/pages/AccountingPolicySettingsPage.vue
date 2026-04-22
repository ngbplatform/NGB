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
  type EntityFormModel,
  type FormMetadataDto,
  getCatalogPage,
  httpPost,
  stableStringify,
  toErrorMessage,
  updateCatalog,
  useMetadataStore,
  useToasts,
} from 'ngb-ui-framework'

const router = useRouter()
const toasts = useToasts()
const metaStore = useMetadataStore()

const catalogType = 'trd.accounting_policy'

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

function cloneForm(form: FormMetadataDto): FormMetadataDto {
  return clonePlainData(form)
}

function buildUiFormFrom(full: FormMetadataDto): FormMetadataDto {
  const out = cloneForm(full)

  const hiddenKeys = new Set<string>([
    'display',
    'inventory_movements_register_id',
    'item_prices_register_id',
  ])

  for (const section of out.sections ?? []) {
    for (const row of section.rows ?? []) {
      row.fields = (row.fields ?? [])
        .filter((field) => !hiddenKeys.has(field.key))
        .map((field) => {
          if (field.key === 'cash_account_id') {
            field.label = 'Default Cash / Bank Account'
            field.helpText = 'Fallback control account used when a payment document does not specify a cash or bank account explicitly.'
          }

          if (field.key === 'ar_account_id') {
            field.label = 'Accounts Receivable Account'
            field.helpText = 'Receivables control account for posted sales invoices and customer returns.'
          }

          if (field.key === 'inventory_account_id') {
            field.label = 'Inventory Asset Account'
            field.helpText = 'Inventory asset account used by receipts, sales COGS relief, transfers, and returns.'
          }

          if (field.key === 'ap_account_id') {
            field.label = 'Accounts Payable Account'
            field.helpText = 'Payables control account for posted purchase receipts and vendor returns.'
          }

          if (field.key === 'sales_revenue_account_id') {
            field.label = 'Sales Revenue Account'
            field.helpText = 'Revenue account credited when posted sales invoices recognize revenue.'
          }

          if (field.key === 'cogs_account_id') {
            field.label = 'Cost of Goods Sold Account'
            field.helpText = 'Expense account debited when inventory leaves stock through posted sales activity.'
          }

          if (field.key === 'inventory_adjustment_account_id') {
            field.label = 'Inventory Adjustment Offset Account'
            field.helpText = 'Offset account used by posted inventory adjustments for write-ups and write-downs.'
          }

          return field
        })
    }
  }

  out.sections = (out.sections ?? [])
    .map((section) => ({
      ...section,
      rows: (section.rows ?? []).filter((row) => (row.fields ?? []).length > 0),
    }))
    .filter((section) => (section.rows ?? []).length > 0)

  if (out.sections.length === 1 && (out.sections[0].title ?? '').toLowerCase() === 'main') {
    out.sections[0].title = 'Settings'
  }

  return out
}

function ensureDisplayValue(): void {
  const value = String(model.value.display ?? '').trim()
  if (!value) model.value.display = 'Accounting Policy'
}

async function copyShareLink(): Promise<void> {
  if (!policy.value?.id) return
  await copyAppLink(router, toasts, { path: '/catalogs/trd.accounting_policy' })
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
  if (!policy.value || !fullMeta.value?.form || saving.value) return

  saving.value = true
  error.value = null

  try {
    ensureDisplayValue()

    await updateCatalog(catalogType, policy.value.id, {
      fields: buildFieldsPayload(fullMeta.value.form, model.value),
    })

    toasts.push({
      title: 'Saved',
      message: 'Trade accounting policy updated.',
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
      message: 'Trade default configuration has been created or refreshed.',
      tone: 'success',
    })
    await load()
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to apply trade default configuration.')
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
  <div class="flex h-full min-h-0 flex-col" data-testid="trade-accounting-policy-page">
    <NgbPageHeader title="Accounting Policy" can-back @back="router.back()">
      <template #secondary>
        <div class="truncate text-xs text-ngb-muted">Trade setup & controls</div>
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
            :disabled="loading || saving || initializing"
            title="Apply defaults"
            @click="applyDefaults"
          >
            <NgbIcon name="check" />
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

    <div class="min-h-0 flex-1 overflow-auto p-6" data-testid="trade-accounting-policy-scroll-region">
      <div
        v-if="error"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
      >
        {{ error }}
      </div>

      <div v-if="loading" class="text-sm text-ngb-muted">Loading…</div>

      <template v-else>
        <div
          v-if="!hasPolicy"
          data-testid="trade-accounting-policy-empty-state"
          class="rounded-[var(--ngb-radius)] border border-ngb-border bg-white p-4 dark:bg-ngb-panel"
        >
          <div class="text-sm font-semibold">Not initialized</div>
          <div class="mt-1 text-sm text-ngb-muted">
            Trade Accounting Policy is created by the setup workflow. Apply defaults to create the policy,
            seed the required Chart of Accounts mappings, and initialize operational register bindings.
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
          <div v-if="uiForm" data-testid="trade-accounting-policy-form">
            <EntityForm :form="uiForm" :model="model" :entity-type-code="catalogType" />
          </div>
          <div v-else class="text-sm text-ngb-muted">No form metadata available.</div>
        </div>
      </template>
    </div>

    <NgbDrawer v-model:open="auditOpen" title="Audit Log" hide-header flush-body>
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
