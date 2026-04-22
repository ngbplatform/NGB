<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import NgbConfirmDialog from '../components/NgbConfirmDialog.vue'
import NgbFormLayout from '../components/forms/NgbFormLayout.vue'
import NgbFormRow from '../components/forms/NgbFormRow.vue'
import NgbValidationSummary from '../components/forms/NgbValidationSummary.vue'
import NgbEntityAuditSidebar from '../editor/NgbEntityAuditSidebar.vue'
import type { EntityEditorFlags } from '../editor/types'
import NgbCheckbox from '../primitives/NgbCheckbox.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import { useToasts } from '../primitives/toast'
import { copyAppLink } from '../router/shareLink'
import { toErrorMessage } from '../utils/errorMessage'
import { stableStringify } from '../utils/stableValue'
import {
  createChartOfAccount,
  getChartOfAccountById,
  markChartOfAccountForDeletion,
  unmarkChartOfAccountForDeletion,
  updateChartOfAccount,
} from './api'
import { buildChartOfAccountsPath } from './navigation'
import type {
  ChartOfAccountsAccountDto,
  ChartOfAccountsCashFlowRoleOptionDto,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsUpsertRequestDto,
  ChartOfAccountEditorShellState,
} from './types'

type ChartOfAccountFieldErrors = {
  code?: string
  name?: string
  accountType?: string
  cashFlowLineCode?: string
}

const AUDIT_ENTITY_KIND_COA_ACCOUNT = 3
const DEFAULT_EDITOR_SHELL: ChartOfAccountEditorShellState = {
  hideHeader: false,
  flushBody: false,
}

const props = withDefaults(defineProps<{
  id?: string | null
  metadata?: ChartOfAccountsMetadataDto | null
  routeBasePath?: string | null
}>(), {
  id: null,
  metadata: null,
  routeBasePath: null,
})

const emit = defineEmits<{
  (e: 'created', id: string): void
  (e: 'saved'): void
  (e: 'changed'): void
  (e: 'state', value: { title: string; subtitle?: string }): void
  (e: 'flags', value: EntityEditorFlags): void
  (e: 'shell', value: ChartOfAccountEditorShellState): void
  (e: 'close'): void
}>()

const router = useRouter()
const route = useRoute()
const toasts = useToasts()

const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const current = ref<ChartOfAccountsAccountDto | null>(null)
const auditOpen = ref(false)
const markConfirmOpen = ref(false)

const form = reactive<ChartOfAccountsUpsertRequestDto>({
  code: '',
  name: '',
  accountType: 'Asset',
  isActive: true,
  cashFlowRole: null,
  cashFlowLineCode: null,
})

const defaultAccountType = computed(() => props.metadata?.accountTypeOptions[0]?.value ?? 'Asset')
const isEditMode = computed(() => !!props.id)
const isMetadataReady = computed(() => !!props.metadata)
const isMarkedForDeletion = computed(() => !!(current.value?.isDeleted || current.value?.isMarkedForDeletion))
const isReadOnly = computed(() => isEditMode.value && isMarkedForDeletion.value)
const resolvedRouteBasePath = computed(() => String(props.routeBasePath ?? '').trim() || route.path)

const typeOptions = computed(() =>
  (props.metadata?.accountTypeOptions ?? []).map((option) => ({
    value: option.value,
    label: option.label,
  })),
)

const cashFlowRoleOptions = computed(() =>
  (props.metadata?.cashFlowRoleOptions ?? []).map((option) => ({
    value: option.value,
    label: option.label,
  })),
)

const selectedCashFlowRoleOption = computed<ChartOfAccountsCashFlowRoleOptionDto | null>(() => {
  const selected = String(form.cashFlowRole ?? '').trim()
  if (!selected) return null
  return props.metadata?.cashFlowRoleOptions.find((option) => option.value === selected) ?? null
})

const compatibleCashFlowLineOptions = computed(() => {
  const selectedRole = String(form.cashFlowRole ?? '').trim()
  if (!selectedRole) return []

  return (props.metadata?.cashFlowLineOptions ?? [])
    .filter((option) => option.allowedRoles.includes(selectedRole))
    .map((option) => ({ value: option.value, label: option.label }))
})

const cashFlowLineOptions = computed(() => {
  const role = selectedCashFlowRoleOption.value
  if (!role?.supportsLineCode) return [{ value: '', label: 'Not applicable' }]

  const prefix = role.requiresLineCode
    ? [{ value: '', label: 'Select line…' }]
    : [{ value: '', label: 'None' }]

  return [...prefix, ...compatibleCashFlowLineOptions.value]
})

const cashFlowLineDisabled = computed(() =>
  loading.value
  || saving.value
  || isReadOnly.value
  || !selectedCashFlowRoleOption.value?.supportsLineCode,
)

const codeValue = computed(() => String(form.code ?? '').trim())
const nameValue = computed(() => String(form.name ?? '').trim())
const accountTypeValue = computed(() => String(form.accountType ?? '').trim())
const cashFlowLineCodeValue = computed(() => String(form.cashFlowLineCode ?? '').trim())

function normalizeOptionalText(value: string | null | undefined): string | null {
  const normalized = String(value ?? '').trim()
  return normalized.length > 0 ? normalized : null
}

function resetForm() {
  form.code = ''
  form.name = ''
  form.accountType = defaultAccountType.value
  form.isActive = true
  form.cashFlowRole = null
  form.cashFlowLineCode = null
}

function applyAccount(account: ChartOfAccountsAccountDto) {
  form.code = account.code
  form.name = account.name
  form.accountType = account.accountType
  form.isActive = account.isActive
  form.cashFlowRole = normalizeOptionalText(account.cashFlowRole)
  form.cashFlowLineCode = normalizeOptionalText(account.cashFlowLineCode)
}

function buildRequest(): ChartOfAccountsUpsertRequestDto {
  const role = normalizeOptionalText(form.cashFlowRole)
  const lineCode = selectedCashFlowRoleOption.value?.supportsLineCode
    ? normalizeOptionalText(form.cashFlowLineCode)
    : null

  return {
    code: codeValue.value,
    name: nameValue.value,
    accountType: accountTypeValue.value || defaultAccountType.value,
    isActive: !!form.isActive,
    cashFlowRole: role,
    cashFlowLineCode: lineCode,
  }
}

const currentSnapshot = computed(() => stableStringify(buildRequest()))
const initialSnapshot = ref('')
const isDirty = computed(() => initialSnapshot.value !== '' && currentSnapshot.value !== initialSnapshot.value)

const fieldErrors = computed<ChartOfAccountFieldErrors>(() => {
  const next: ChartOfAccountFieldErrors = {}

  if (!codeValue.value) next.code = 'Code is required.'
  if (!nameValue.value) next.name = 'Name is required.'
  if (!accountTypeValue.value) next.accountType = 'Type is required.'
  if (selectedCashFlowRoleOption.value?.requiresLineCode && !cashFlowLineCodeValue.value) {
    next.cashFlowLineCode = 'Cash Flow Line Code is required for the selected role.'
  }

  return next
})

const validationMessages = computed(() =>
  Object.values(fieldErrors.value).filter((message): message is string => typeof message === 'string' && message.length > 0),
)

const canSave = computed(() =>
  isMetadataReady.value
  && !loading.value
  && !saving.value
  && validationMessages.value.length === 0
  && (!isEditMode.value || !isMarkedForDeletion.value),
)

const title = computed(() => {
  if (!isEditMode.value) return 'New account'

  const code = String(current.value?.code ?? '').trim()
  const name = String(current.value?.name ?? '').trim()
  if (code && name) return `${code} — ${name}`
  return code || name || 'Account'
})

const auditEntityTitle = computed(() => {
  const code = String(current.value?.code ?? '').trim()
  const name = String(current.value?.name ?? '').trim()
  if (code && name) return `${code} — ${name}`
  return code || name || null
})

const flags = computed<EntityEditorFlags>(() => ({
  canSave: canSave.value,
  isDirty: isDirty.value,
  loading: loading.value || !isMetadataReady.value,
  saving: saving.value,
  canExpand: false,
  canDelete: false,
  canMarkForDeletion: isEditMode.value && !isMarkedForDeletion.value && !loading.value && !saving.value,
  canUnmarkForDeletion: isEditMode.value && isMarkedForDeletion.value && !loading.value && !saving.value,
  canPost: false,
  canUnpost: false,
  canShowAudit: isEditMode.value && !!current.value?.accountId,
  canShareLink: isEditMode.value && !!current.value?.accountId,
}))

watch(
  () => title.value,
  () => emit('state', { title: title.value }),
  { immediate: true },
)

watch(
  () => flags.value,
  (value) => emit('flags', value),
  { immediate: true },
)

watch(
  () => auditOpen.value,
  (open) => emit('shell', open ? { hideHeader: true, flushBody: true } : DEFAULT_EDITOR_SHELL),
  { immediate: true },
)

watch(
  () => [
    selectedCashFlowRoleOption.value?.supportsLineCode ?? false,
    compatibleCashFlowLineOptions.value.map((option) => option.value).join('|'),
  ],
  ([supportsLineCode]) => {
    if (!supportsLineCode) {
      form.cashFlowLineCode = null
      return
    }

    if (
      form.cashFlowLineCode
      && !compatibleCashFlowLineOptions.value.some((option) => option.value === form.cashFlowLineCode)
    ) {
      form.cashFlowLineCode = null
    }
  },
)

watch(
  () => typeOptions.value.map((option) => option.value).join('|'),
  () => {
    if (current.value) return
    if (typeOptions.value.length === 0) return
    if (!typeOptions.value.some((option) => option.value === form.accountType)) {
      form.accountType = defaultAccountType.value
    }
    if (!isDirty.value) {
      initialSnapshot.value = currentSnapshot.value
    }
  },
  { immediate: true },
)

let loadSeq = 0

function initializeCreateMode() {
  loadSeq += 1
  loading.value = false
  error.value = null
  current.value = null
  auditOpen.value = false
  resetForm()
  initialSnapshot.value = currentSnapshot.value
}

async function loadAccount(accountId: string) {
  const seq = ++loadSeq
  loading.value = true
  error.value = null
  current.value = null
  auditOpen.value = false

  try {
    const account = await getChartOfAccountById(accountId)
    if (seq !== loadSeq) return
    current.value = account
    applyAccount(account)
    initialSnapshot.value = currentSnapshot.value
  } catch (cause) {
    if (seq !== loadSeq) return
    error.value = toErrorMessage(cause, 'Failed to load the account.')
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

watch(
  () => props.id,
  (nextId) => {
    if (!nextId) {
      initializeCreateMode()
      return
    }

    void loadAccount(nextId)
  },
  { immediate: true },
)

function buildAccountShareTarget(accountId: string) {
  return buildChartOfAccountsPath({
    panel: 'edit',
    id: accountId,
    basePath: resolvedRouteBasePath.value,
  })
}

async function copyShareLink() {
  const accountId = current.value?.accountId
  if (!accountId) return false

  return await copyAppLink(
    router,
    toasts,
    buildAccountShareTarget(accountId),
    { message: 'Shareable account link copied to clipboard.' },
  )
}

function openAuditLog() {
  if (!current.value?.accountId) return
  auditOpen.value = true
}

function closeAuditLog() {
  auditOpen.value = false
}

function requestMarkForDeletion() {
  if (!flags.value.canMarkForDeletion) return
  markConfirmOpen.value = true
}

function cancelMarkForDeletion() {
  markConfirmOpen.value = false
}

async function save() {
  if (!canSave.value) return

  saving.value = true
  error.value = null

  try {
    const request = buildRequest()

    if (!isEditMode.value) {
      const created = await createChartOfAccount(request)
      current.value = created
      applyAccount(created)
      initialSnapshot.value = currentSnapshot.value
      emit('created', created.accountId)
      return
    }

    const accountId = current.value?.accountId ?? props.id
    if (!accountId) throw new Error('Missing accountId')

    const updated = await updateChartOfAccount(accountId, request)
    current.value = updated
    applyAccount(updated)
    initialSnapshot.value = currentSnapshot.value
    emit('saved')
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to save the account.')
  } finally {
    saving.value = false
  }
}

async function markForDeletion() {
  const accountId = current.value?.accountId
  if (!accountId || saving.value) return

  saving.value = true
  error.value = null

  try {
    await markChartOfAccountForDeletion(accountId)
    current.value = current.value
      ? {
          ...current.value,
          isMarkedForDeletion: true,
        }
      : null
    auditOpen.value = false
    emit('changed')
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to mark the account for deletion.')
  } finally {
    saving.value = false
  }
}

async function confirmMarkForDeletion() {
  markConfirmOpen.value = false
  await markForDeletion()
}

async function unmarkForDeletion() {
  const accountId = current.value?.accountId
  if (!accountId || saving.value) return

  saving.value = true
  error.value = null

  try {
    await unmarkChartOfAccountForDeletion(accountId)
    current.value = current.value
      ? {
          ...current.value,
          isMarkedForDeletion: false,
          isDeleted: false,
        }
      : null
    auditOpen.value = false
    emit('changed')
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to restore the account.')
  } finally {
    saving.value = false
  }
}

function toggleMarkForDeletion() {
  if (flags.value.canUnmarkForDeletion) {
    void unmarkForDeletion()
    return
  }

  if (flags.value.canMarkForDeletion) requestMarkForDeletion()
}

defineExpose({
  save,
  copyShareLink,
  openAuditLog,
  closeAuditLog,
  toggleMarkForDeletion,
})
</script>

<template>
  <NgbEntityAuditSidebar
    v-if="auditOpen"
    :open="auditOpen"
    :entity-kind="AUDIT_ENTITY_KIND_COA_ACCOUNT"
    :entity-id="current?.accountId ?? null"
    :entity-title="auditEntityTitle"
    @back="closeAuditLog"
    @close="emit('close')"
  />

  <template v-else>
    <div v-if="error" class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100">
      {{ error }}
    </div>

    <NgbValidationSummary
      v-if="validationMessages.length > 0 && !loading && isMetadataReady"
      :messages="validationMessages"
    />

    <div v-if="loading || !isMetadataReady" class="mt-4 text-sm text-ngb-muted">
      Loading…
    </div>

    <div v-else data-testid="chart-of-account-editor" class="mt-4">
      <div
        v-if="isEditMode && isMarkedForDeletion"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm dark:border-red-900/50 dark:bg-red-950/30"
      >
        <div class="font-semibold text-ngb-danger">Deleted</div>
        <div class="mt-1 text-ngb-muted">This account is marked for deletion. Restore it to edit.</div>
      </div>

      <NgbFormLayout>
        <NgbFormRow label="Code" :error="fieldErrors.code">
          <NgbInput
            v-model="form.code"
            placeholder="e.g. 1000"
            :disabled="saving || isReadOnly"
          />
        </NgbFormRow>

        <NgbFormRow label="Name" :error="fieldErrors.name">
          <NgbInput
            v-model="form.name"
            placeholder="e.g. Cash"
            :disabled="saving || isReadOnly"
          />
        </NgbFormRow>

        <NgbFormRow label="Type" :error="fieldErrors.accountType">
          <NgbSelect
            v-model="form.accountType"
            :options="typeOptions"
            :disabled="saving || isReadOnly"
          />
        </NgbFormRow>

        <NgbFormRow label="Active">
          <div class="flex h-9 items-center">
            <NgbCheckbox
              v-model="form.isActive"
              :disabled="saving || isReadOnly"
            />
          </div>
        </NgbFormRow>

        <NgbFormRow label="Cash Flow Role">
          <NgbSelect
            v-model="form.cashFlowRole"
            :options="cashFlowRoleOptions"
            :disabled="saving || isReadOnly"
          />
        </NgbFormRow>

        <NgbFormRow
          label="Cash Flow Line Code"
          :hint="selectedCashFlowRoleOption?.requiresLineCode ? 'Required for the selected role.' : undefined"
          :error="fieldErrors.cashFlowLineCode"
        >
          <NgbSelect
            v-model="form.cashFlowLineCode"
            :options="cashFlowLineOptions"
            :disabled="cashFlowLineDisabled"
          />
        </NgbFormRow>
      </NgbFormLayout>
    </div>
  </template>

  <NgbConfirmDialog
    :open="markConfirmOpen"
    title="Mark for deletion?"
    :message="isDirty
      ? 'This account will be marked for deletion. Unsaved changes will be lost.'
      : 'This account will be marked for deletion. You can restore it later.'"
    confirm-text="Mark"
    cancel-text="Cancel"
    danger
    @update:open="(value) => (!value ? cancelMarkForDeletion() : null)"
    @confirm="confirmMarkForDeletion"
  />
</template>
