<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  type CatalogItemDto,
  type CatalogTypeMetadataDto,
  buildCatalogFullPageUrl,
  buildDocumentFullPageUrl,
  type DocumentDto,
  type DocumentEffectsDto,
  type DocumentStatus,
  type DocumentTypeMetadataDto,
  NgbEntityEditor,
  navigateBack,
  normalizeDocumentStatusValue,
  type RecordPayload,
  runEntityEditorAction,
  resolveNavigateOnCreate,
  stableStringify,
  useEntityEditorBusinessContext,
  useEntityEditorCapabilities,
  useEntityEditorCommandPalette,
  useConfiguredEntityEditorDocumentActions,
  useEntityEditorHeaderActions,
  useEntityEditorLifecycleConfirmations,
  useEntityEditorLeaveGuard,
  useEntityEditorNavigationActions,
  useEntityEditorOutputs,
  useEntityEditorPageActions,
  useEntityEditorPersistence,
  useLookupStore,
  type EditorChangeReason,
  type EditorKind,
  type EditorMode,
  type EntityEditorFlags,
  type EntityEditorHandle,
  type EntityEditorRenderExtension,
  type EntityFormModel,
  type EntityHeaderIconAction,
  useMetadataStore,
  useToasts,
} from '@ngbplatform/ui'
import LeaseTenantsGrid from '../../components/lease/LeaseTenantsGrid.vue'
import PmPropertyBulkCreateUnitsDialog from '../../components/property/PmPropertyBulkCreateUnitsDialog.vue'
import { PM_EDITOR_TAGS } from '../entityProfile'
import type { PmEntityEditorHandle } from '../types'
import { useCatalogEntityEditorPersistence } from './useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from './useDocumentEntityEditorPersistence'
import { useEntityEditorErrorState } from './useEntityEditorErrorState'
import { useEntityEditorLeasePart } from './useEntityEditorLeasePart'
import type { PmEntityEditorPersistenceContext } from './pmEntityEditorPersistenceContext'
import { usePmCatalogEntityEditorCapabilities } from './usePmCatalogEntityEditorCapabilities'

const props = withDefaults(
  defineProps<{
    kind: EditorKind
    typeCode: string
    id?: string | null
    mode?: EditorMode
    canBack?: boolean
    initialFields?: EntityFormModel | null
    initialParts?: RecordPayload['parts'] | null
    expandTo?: string | null
    compactTo?: string | null
    closeTo?: string | null
    navigateOnCreate?: boolean
  }>(),
  {
    mode: 'page',
    canBack: true,
    initialFields: null,
    initialParts: null,
    expandTo: null,
    compactTo: null,
    closeTo: null,
    navigateOnCreate: undefined,
  },
)

const emit = defineEmits<{
  (e: 'created', id: string): void
  (e: 'saved'): void
  (e: 'changed', reason?: EditorChangeReason): void
  (e: 'deleted'): void
  (e: 'state', value: { title: string; subtitle?: string }): void
  (e: 'flags', value: EntityEditorFlags): void
  (e: 'close'): void
}>()

const route = useRoute()
const router = useRouter()
const metaStore = useMetadataStore()
const lookupStore = useLookupStore()
const toasts = useToasts()

const editorKind = computed(() => props.kind)
const editorTypeCode = computed(() => props.typeCode)
const editorMode = computed(() => props.mode)
const editorInitialFields = computed(() => props.initialFields)
const editorInitialParts = computed(() => props.initialParts)
const editorExpandTo = computed(() => props.expandTo)
const editorCompactTo = computed(() => props.compactTo)
const editorCloseTo = computed(() => props.closeTo)
const editorNavigateOnCreate = computed(() => props.navigateOnCreate)

const currentId = ref<string | null>(props.id ?? null)
watch(
  () => props.id,
  (value) => {
    currentId.value = value ?? null
  },
)

const isNew = computed(() => !currentId.value)
const loading = ref(false)
const saving = ref(false)
const editorShellRef = ref<{ focusField?: (path: string) => boolean; focusFirstError?: (keys: string[]) => boolean } | null>(null)
const leaseGridRef = ref<InstanceType<typeof LeaseTenantsGrid> | null>(null)

const catalogMeta = ref<CatalogTypeMetadataDto | null>(null)
const docMeta = ref<DocumentTypeMetadataDto | null>(null)
const metadata = computed<CatalogTypeMetadataDto | DocumentTypeMetadataDto | null>(() =>
  editorKind.value === 'catalog' ? catalogMeta.value : docMeta.value,
)

const catalogItem = ref<CatalogItemDto | null>(null)
const doc = ref<DocumentDto | null>(null)
const docEffects = ref<DocumentEffectsDto | null>(null)
const model = ref<EntityFormModel>({})
const currentIdValue = computed(() => currentId.value)

const status = computed<DocumentStatus>(() => normalizeDocumentStatusValue(doc.value?.status ?? 1))
const isDraft = computed(() => status.value === 1)
const isDeletedStatus = computed(() => status.value === 3)

const isMarkedForDeletion = computed(() => {
  if (editorKind.value === 'catalog') {
    return !!(catalogItem.value?.isMarkedForDeletion || catalogItem.value?.isDeleted)
  }

  return !!doc.value?.isMarkedForDeletion || isDeletedStatus.value
})

const {
  currentEditorContext,
  hasTag,
} = useEntityEditorBusinessContext({
  kind: editorKind,
  typeCode: editorTypeCode,
  model,
  docMeta,
  loading,
  isNew,
  isDraft,
  isMarkedForDeletion,
})

const isPmPropertyCatalog = computed(() => hasTag(PM_EDITOR_TAGS.PROPERTY_CATALOG))
const isLeaseDocument = computed(() => hasTag(PM_EDITOR_TAGS.LEASE_DOCUMENT))

const leaseEditor = useEntityEditorLeasePart({ isLeaseDocument })
const { leasePartiesRows, buildCopyParts } = leaseEditor

const {
  canOpenAudit,
  canShareLink,
  canOpenEffectsPage,
  canOpenDocumentFlowPage,
  canPrintDocument,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canDelete,
  canSave,
  documentStatusLabel,
  documentStatusTone,
  title,
  subtitle,
  auditEntityKind,
  auditEntityId,
  auditEntityTitle,
  isReadOnly,
} = useEntityEditorCapabilities({
  kind: editorKind,
  currentId,
  metadata,
  model,
  loading,
  saving,
  isNew,
  isDraft,
  isMarkedForDeletion,
  status,
})

const {
  canBulkCreateUnits,
} = usePmCatalogEntityEditorCapabilities({
  model,
  loading,
  saving,
  isNew,
  isMarkedForDeletion,
  isPmPropertyCatalog,
})

const fieldLabels = computed<Record<string, string>>(() => {
  const labels: Record<string, string> = {}
  for (const section of metadata.value?.form?.sections ?? []) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) {
        if (field?.key && typeof field.label === 'string' && field.label.trim().length > 0) {
          labels[field.key] = field.label
        }
      }
    }
  }
  return labels
})

const {
  error,
  displayedError,
  inlineFieldErrors,
  leaseTenantValidation,
  bannerIssues,
  normalizeEditorError,
  setEditorError,
  dismissFieldIssues,
  dismissLeaseIssues,
} = useEntityEditorErrorState({
  fieldLabels,
  isLeaseDocument,
  leasePartiesRows,
  loading,
  saving,
  formRef: editorShellRef,
  leaseGridRef,
})

watch(
  () => ({ ...model.value }),
  (next, prev) => {
    if (!prev || loading.value || saving.value || !error.value) return

    const changedKeys = new Set<string>()
    for (const key of new Set([...Object.keys(prev), ...Object.keys(next)])) {
      if (stableStringify(prev[key]) !== stableStringify(next[key])) changedKeys.add(key)
    }

    for (const key of changedKeys) dismissFieldIssues(key)
  },
)

watch(
  () => stableStringify(leasePartiesRows.value),
  (next, prev) => {
    if (!prev || next === prev || loading.value || saving.value || !error.value) return
    dismissLeaseIssues()
  },
)

const initialSnapshot = ref('')
const currentSnapshot = computed(() =>
  stableStringify({
    fields: model.value,
    parties: isLeaseDocument.value ? leasePartiesRows.value : null,
  }),
)
const isDirty = computed(() => initialSnapshot.value !== '' && currentSnapshot.value !== initialSnapshot.value)

const {
  leaveOpen,
  requestNavigate,
  requestClose,
  confirmLeave,
  cancelLeave,
} = useEntityEditorLeaveGuard({
  isDirty,
  loading,
  saving,
  router,
  onClose: () => emit('close'),
})

function resetInitialSnapshot() {
  initialSnapshot.value = stableStringify({
    fields: model.value,
    parties: isLeaseDocument.value ? leasePartiesRows.value : null,
  })
}

const bulkUnitsOpen = ref(false)

function openBulkCreateUnitsWizard() {
  if (!canBulkCreateUnits.value) return
  bulkUnitsOpen.value = true
}

const persistenceContext: PmEntityEditorPersistenceContext = {
  kind: editorKind,
  typeCode: editorTypeCode,
  currentId,
  isNew,
  metadata,
  catalogMeta,
  docMeta,
  catalogItem,
  doc,
  docEffects,
  model,
  lookupStore,
  initialFields: editorInitialFields,
  initialParts: editorInitialParts,
  leaseEditor,
  currentEditorContext,
  ensureCatalogMetadata: (typeCode) => metaStore.ensureCatalogType(typeCode),
  ensureDocumentMetadata: (typeCode) => metaStore.ensureDocumentType(typeCode),
  resetInitialSnapshot,
  setEditorError,
  onCreated: async (id) => {
    emit('created', id)
    if (!resolveNavigateOnCreate(editorNavigateOnCreate.value, editorMode.value)) return
    const url = editorKind.value === 'catalog'
      ? buildCatalogFullPageUrl(editorTypeCode.value, id)
      : buildDocumentFullPageUrl(editorTypeCode.value, id)
    await router.replace(url)
  },
  onSaved: () => emit('saved'),
}

const {
  load,
  save,
  markForDeletion: persistMarkForDeletion,
  unmarkForDeletion: persistUnmarkForDeletion,
  deleteEntity,
  loadDocumentEffectsSnapshot,
} = useEntityEditorPersistence({
  kind: editorKind,
  typeCode: editorTypeCode,
  metadata,
  loading,
  saving,
  canSave,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canDelete,
  isNew,
  setEditorError,
  normalizeEditorError,
  emitChanged: (reason) => emit('changed', reason),
  emitDeleted: () => emit('deleted'),
  onMarkedForDeletion: () => toasts.push({ title: 'Deleted', message: 'Record marked for deletion.', tone: 'warn' }),
  onUnmarkedForDeletion: () => toasts.push({ title: 'Restored', message: 'Record restored.', tone: 'success' }),
  adapters: {
    catalog: useCatalogEntityEditorPersistence(persistenceContext),
    document: useDocumentEntityEditorPersistence(persistenceContext),
  },
})

const {
  markConfirmOpen,
  markConfirmMessage,
  requestMarkForDeletion,
  cancelMarkForDeletion,
  confirmMarkForDeletion,
} = useEntityEditorLifecycleConfirmations({
  kind: editorKind,
  isDirty,
  canMarkForDeletion,
  onMarkForDeletion: persistMarkForDeletion,
})

const {
  auditOpen,
  fallbackCloseTarget,
  copyShareLink,
  copyDocument,
  openDocumentPrintPage,
  openAuditLog,
  closeAuditLog,
  openDocumentEffectsPage,
  openDocumentFlowPage,
  openFullPage,
  openCompactPage,
  closePage,
} = useEntityEditorNavigationActions({
  kind: editorKind,
  typeCode: editorTypeCode,
  mode: editorMode,
  compactTo: editorCompactTo,
  expandTo: editorExpandTo,
  closeTo: editorCloseTo,
  currentId,
  metadata,
  docMeta,
  model,
  loading,
  saving,
  canOpenAudit,
  canPrintDocument,
  canOpenDocumentFlowPage,
  canOpenEffectsPage,
  requestNavigate,
  requestClose,
  router,
  route,
  toasts,
  buildCopyParts,
})

function toggleMarkForDeletion() {
  if (editorKind.value === 'document') {
    if (isDocumentActionAllowed('unmark_for_deletion')) requestDocumentAction('unmark_for_deletion')
    else requestDocumentAction('mark_for_deletion')
    return
  }
  if (canUnmarkForDeletion.value) void persistUnmarkForDeletion()
  else if (canMarkForDeletion.value) requestMarkForDeletion()
}

function togglePost() {
  if (isDocumentActionAllowed('unpost')) requestDocumentAction('unpost')
  else requestDocumentAction('post')
}

const {
  documentLifecycleActions,
  extraPrimaryActions: configuredDocumentPrimaryActions,
  extraMoreActionGroups: configuredDocumentMoreActionGroups,
  handleConfiguredAction,
  requestDocumentAction,
  isDocumentActionAllowed,
  confirmation: documentActionConfirmation,
  cancelDocumentActionConfirmation,
  confirmDocumentAction,
  executingDocumentAction,
  refreshDocumentActions,
} = useConfiguredEntityEditorDocumentActions({
  kind: editorKind,
  typeCode: editorTypeCode,
  currentId: currentIdValue,
  loading: computed(() => loading.value),
  saving: computed(() => saving.value),
  requestNavigate,
  setEditorError,
  normalizeEditorError,
  localActionHandlers: {
    view_effects: openDocumentEffectsPage,
    view_flow: openDocumentFlowPage,
    view_audit: openAuditLog,
    print: openDocumentPrintPage,
  },
  beforeExecute: async (actionCode) => {
    if (actionCode !== 'post' || !isDirty.value) return true
    await save()
    return { proceed: !error.value && !isDirty.value, refreshState: true }
  },
  applyActionDocument: (document) => {
    doc.value = document
    model.value = { ...(document.payload?.fields ?? {}) }
    resetInitialSnapshot()
    emit('changed')
  },
})

const canPost = computed(() => isDocumentActionAllowed('post'))
const canUnpost = computed(() => isDocumentActionAllowed('unpost'))

async function markForDeletion() {
  if (editorKind.value === 'document') {
    requestDocumentAction('mark_for_deletion')
    return
  }
  await persistMarkForDeletion()
}

async function unmarkForDeletion() {
  if (editorKind.value === 'document') {
    requestDocumentAction('unmark_for_deletion')
    return
  }
  await persistUnmarkForDeletion()
}

async function post() {
  requestDocumentAction('post')
}

async function unpost() {
  requestDocumentAction('unpost')
}

watch(status, () => {
  if (editorKind.value !== 'document' || !currentId.value) return
  void refreshDocumentActions().catch((cause) => {
    setEditorError(normalizeEditorError(cause))
  })
})

const extraPageActions = computed<EntityHeaderIconAction[]>(() =>
  canBulkCreateUnits.value
    ? [{
        key: 'openBulkCreateUnits',
        title: 'Bulk create units',
        icon: 'grid',
        disabled: loading.value || saving.value,
      }]
    : [],
)

const {
  documentPrimaryActions,
  documentMoreActionGroups,
  handleDocumentHeaderAction,
} = useEntityEditorHeaderActions({
  kind: editorKind,
  mode: editorMode,
  compactTo: editorCompactTo,
  expandTo: editorExpandTo,
  currentId: currentIdValue,
  loading: computed(() => loading.value),
  saving: computed(() => saving.value || executingDocumentAction.value),
  isNew,
  isMarkedForDeletion,
  canSave,
  canShareLink,
  onOpenCompactPage: openCompactPage,
  onOpenFullPage: openFullPage,
  onCopyDocument: copyDocument,
  onSave: save,
  onCopyShareLink: copyShareLink,
  documentLifecycleActions,
  extraPrimaryActions: configuredDocumentPrimaryActions,
  extraMoreActionGroups: configuredDocumentMoreActionGroups,
  onUnhandledAction: (action) => {
    handleConfiguredAction(action)
  },
})

useEntityEditorCommandPalette({
  mode: editorMode,
  kind: editorKind,
  typeCode: editorTypeCode,
  currentId: currentIdValue,
  title,
  isDocumentActionAllowed,
  requestDocumentAction,
})

const pageActions = useEntityEditorPageActions({
  kind: editorKind,
  mode: editorMode,
  compactTo: editorCompactTo,
  loading: computed(() => loading.value),
  saving: computed(() => saving.value),
  isNew,
  isMarkedForDeletion,
  canSave,
  canShareLink,
  canOpenAudit,
  canMarkForDeletion,
  canUnmarkForDeletion,
  extraActions: extraPageActions,
})

const pageActionHandlers = {
  openBulkCreateUnits: openBulkCreateUnitsWizard,
  openCompactPage,
  copyShareLink: async () => {
    await copyShareLink()
  },
  openAuditLog,
  toggleMarkForDeletion,
  save: async () => {
    await save()
  },
}

function handleHeaderAction(action: string) {
  if (editorKind.value === 'document') {
    handleDocumentHeaderAction(action)
    return
  }

  runEntityEditorAction(action, pageActionHandlers)
}

const { flags } = useEntityEditorOutputs({
  emit,
  title,
  subtitle,
  isDirty,
  loading: computed(() => loading.value),
  saving: computed(() => saving.value),
  canExpand: computed(() => !!props.expandTo),
  canDelete,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canPost,
  canUnpost,
  canOpenAudit,
  canShareLink,
  canSave,
  extraFlags: computed(() => ({
    bulkCreateUnits: canBulkCreateUnits.value,
  })),
})

watch(
  () => [editorKind.value, editorTypeCode.value, currentId.value],
  () => void load(),
  { immediate: true },
)

function setLeaseGridRef(value: unknown) {
  leaseGridRef.value = value as InstanceType<typeof LeaseTenantsGrid> | null
}

const afterFormExtensions = computed<EntityEditorRenderExtension[]>(() => {
  if (!(editorKind.value === 'document' && isLeaseDocument.value && metadata.value?.parts && !loading.value)) {
    return []
  }

  return [{
    key: 'lease-tenants-grid',
    component: LeaseTenantsGrid,
    componentRef: setLeaseGridRef,
    props: {
      modelValue: leasePartiesRows.value,
      'onUpdate:modelValue': (value: typeof leasePartiesRows.value) => {
        leasePartiesRows.value = value
      },
      readonly: isReadOnly.value,
      errors: leaseTenantValidation.value,
    },
  }]
})

const dialogExtensions = computed<EntityEditorRenderExtension[]>(() => {
  if (!(isPmPropertyCatalog.value && !isNew.value && currentId.value)) return []

  return [{
    key: 'pm-property-bulk-create-units',
    component: PmPropertyBulkCreateUnitsDialog,
    props: {
      open: bulkUnitsOpen.value,
      'onUpdate:open': (value: boolean) => {
        bulkUnitsOpen.value = value
      },
      buildingId: currentId.value,
      buildingDisplay: String(model.value.display ?? ''),
    },
  }]
})

const exposedHandle = {
  save,
  load,
  openFullPage,
  openCompactPage,
  closePage,
  toggleMarkForDeletion,
  togglePost,
  markForDeletion,
  unmarkForDeletion,
  deleteEntity,
  post,
  unpost,
  getDocumentEffects: () => docEffects.value,
  reloadDocumentEffects: async () => {
    if (editorKind.value !== 'document' || !currentId.value) return null
    await loadDocumentEffectsSnapshot(editorTypeCode.value, currentId.value)
    return docEffects.value
  },
  copyShareLink,
  copyDocument,
  printDocument: openDocumentPrintPage,
  openAuditLog,
  openAudit: openAuditLog,
  closeAuditLog,
  getIsDirty: () => isDirty.value,
  getCanSave: () => canSave.value,
  getFlags: () => flags.value,
} satisfies EntityEditorHandle<DocumentEffectsDto | null>

defineExpose({
  ...exposedHandle,
  openBulkCreateUnitsWizard,
} satisfies PmEntityEditorHandle)
</script>

<template>
  <NgbEntityEditor
    ref="editorShellRef"
    :kind="kind"
    :mode="mode"
    :can-back="canBack"
    :title="title"
    :subtitle="subtitle"
    :document-status-label="documentStatusLabel"
    :document-status-tone="documentStatusTone"
    :loading="loading"
    :saving="saving || executingDocumentAction"
    :page-actions="pageActions"
    :document-primary-actions="documentPrimaryActions"
    :document-more-action-groups="documentMoreActionGroups"
    :is-new="isNew"
    :is-marked-for-deletion="isMarkedForDeletion"
    :displayed-error="displayedError"
    :banner-issues="bannerIssues"
    :form="metadata?.form ?? null"
    :model="model"
    :entity-type-code="typeCode"
    :status="kind === 'document' ? status : undefined"
    :is-read-only="isReadOnly"
    :errors="inlineFieldErrors"
    :after-form-extensions="afterFormExtensions"
    :dialog-extensions="dialogExtensions"
    :audit-open="auditOpen"
    :audit-entity-kind="auditEntityKind"
    :audit-entity-id="auditEntityId"
    :audit-entity-title="auditEntityTitle"
    :leave-open="leaveOpen"
    :mark-confirm-open="markConfirmOpen"
    :mark-confirm-message="markConfirmMessage"
    :document-action-confirmation="documentActionConfirmation"
    @back="navigateBack(router, route, props.closeTo ?? fallbackCloseTarget)"
    @close="closePage"
    @action="handleHeaderAction"
    @closeAuditLog="closeAuditLog"
    @cancelLeave="cancelLeave"
    @confirmLeave="confirmLeave"
    @cancelMarkForDeletion="cancelMarkForDeletion"
    @confirmMarkForDeletion="confirmMarkForDeletion"
    @cancelDocumentAction="cancelDocumentActionConfirmation"
    @confirmDocumentAction="confirmDocumentAction"
  />
</template>
