<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  clonePlainData,
  type CatalogItemDto,
  type CatalogTypeMetadataDto,
  type DocumentDto,
  type DocumentEffectsDto,
  type DocumentStatus,
  type DocumentTypeMetadataDto,
  NgbEntityEditor,
  navigateBack,
  normalizeDocumentStatusValue,
  normalizeEntityEditorError,
  type RecordPayload,
  runEntityEditorAction,
  stableStringify,
  useConfiguredEntityEditorDocumentActions,
  useEntityEditorBusinessContext,
  useEntityEditorCapabilities,
  useEntityEditorCommandPalette,
  useEntityEditorHeaderActions,
  useEntityEditorLeaveGuard,
  useEntityEditorNavigationActions,
  useEntityEditorOutputs,
  useEntityEditorPageActions,
  useEntityEditorPersistence,
  useLookupStore,
  type EditorChangeReason,
  type EditorErrorIssue,
  type EditorErrorState,
  type EditorKind,
  type EditorMode,
  type EntityEditorFlags,
  type EntityEditorHandle,
  humanizeEntityEditorFieldKey,
  isEntityEditorFormIssuePath,
  type EntityEditorRenderExtension,
  type EntityFormModel,
  useMetadataStore,
  useToasts,
} from 'ngb-ui-framework'

import { agencyBillingMetadataFormBehavior } from '../metadata/framework'
import type { AgencyBillingDocumentPartErrors } from './documentParts'
import AgencyBillingDocumentPartsEditor from './AgencyBillingDocumentPartsEditor.vue'
import type { AgencyBillingEntityEditorPersistenceContext } from './agencyBillingEntityEditorPersistenceContext'
import { useCatalogEntityEditorPersistence } from './useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from './useDocumentEntityEditorPersistence'

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

const catalogMeta = ref<CatalogTypeMetadataDto | null>(null)
const docMeta = ref<DocumentTypeMetadataDto | null>(null)
const metadata = computed<CatalogTypeMetadataDto | DocumentTypeMetadataDto | null>(() =>
  editorKind.value === 'catalog' ? catalogMeta.value : docMeta.value,
)

const catalogItem = ref<CatalogItemDto | null>(null)
const doc = ref<DocumentDto | null>(null)
const docEffects = ref<DocumentEffectsDto | null>(null)
const model = ref<EntityFormModel>({})
const partsModel = ref<RecordPayload['parts'] | null>(clonePlainData(props.initialParts ?? null))
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

const { currentEditorContext } = useEntityEditorBusinessContext({
  kind: editorKind,
  typeCode: editorTypeCode,
  model,
  docMeta,
  loading,
  isNew,
  isDraft,
  isMarkedForDeletion,
})

const docEffectsUi = computed(() => docEffects.value?.ui ?? null)
const {
  canOpenAudit,
  canShareLink,
  canOpenEffectsPage,
  canOpenDocumentFlowPage,
  canPrintDocument,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canDelete,
  canPost,
  canUnpost,
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

const error = ref<EditorErrorState | null>(null)

function resolveIssueLabel(path: string): string {
  const raw = String(path ?? '').trim()
  if (isEntityEditorFormIssuePath(raw)) return 'Validation'
  return fieldLabels.value[raw] ?? humanizeEntityEditorFieldKey(raw)
}

function normalizeEditorError(cause: unknown): EditorErrorState {
  return normalizeEntityEditorError(cause, { resolveIssueLabel })
}

const displayedError = computed(() => error.value)

const inlineFieldErrors = computed<Record<string, string>>(() => {
  const result: Record<string, string> = {}

  for (const issue of displayedError.value?.issues ?? []) {
    if (isEntityEditorFormIssuePath(issue.path)) continue
    if (issue.scope !== 'field') continue
    if (!(issue.path in fieldLabels.value)) continue
    if (result[issue.path]) continue

    const first = issue.messages.find((message) => typeof message === 'string' && message.trim().length > 0)
    if (first) result[issue.path] = first
  }

  return result
})

const partErrors = computed<AgencyBillingDocumentPartErrors>(() => {
  const result: AgencyBillingDocumentPartErrors = {}

  for (const issue of displayedError.value?.issues ?? []) {
    const match = /^parts\.([^.]+)\.rows\[(\d+)\]\.([^.]+)$/.exec(issue.path)
    if (!match) continue

    const [, partCode, rowIndexRaw, fieldKey] = match
    const rowIndex = Number.parseInt(rowIndexRaw, 10)
    const message = issue.messages.find((entry) => typeof entry === 'string' && entry.trim().length > 0)
    if (!Number.isFinite(rowIndex) || !message) continue

    result[partCode] ??= {}
    result[partCode][rowIndex] ??= {}
    result[partCode][rowIndex][fieldKey] ??= message
  }

  return result
})

const bannerIssues = computed<EditorErrorIssue[]>(() => displayedError.value?.issues ?? [])

function focusFirstValidationError(state: EditorErrorState | null) {
  if (!state) return

  for (const issue of state.issues) {
    if (isEntityEditorFormIssuePath(issue.path)) continue
    if (editorShellRef.value?.focusField?.(issue.path)) return
  }

  const keys = Object.keys(inlineFieldErrors.value)
  if (keys.length > 0) {
    void editorShellRef.value?.focusFirstError?.(keys)
  }
}

function setEditorError(value: EditorErrorState | null) {
  error.value = value
  if (!value) return

  void nextTick(() => {
    focusFirstValidationError(value)
  })
}

const initialSnapshot = ref('')
const currentSnapshot = computed(() =>
  stableStringify({
    fields: model.value,
    parts: partsModel.value,
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
    parts: partsModel.value,
  })
}

const markConfirmOpen = ref(false)
const markConfirmMessage = computed(() => {
  const base = editorKind.value === 'catalog' ? 'record' : 'document'
  const extra = isDirty.value ? ' Unsaved changes will be lost.' : ''
  return `This will mark the ${base} for deletion.${extra}`
})

function requestMarkForDeletion() {
  if (!canMarkForDeletion.value) return
  markConfirmOpen.value = true
}

function cancelMarkForDeletion() {
  markConfirmOpen.value = false
}

const persistenceContext: AgencyBillingEntityEditorPersistenceContext = {
  kind: editorKind,
  typeCode: editorTypeCode,
  mode: editorMode,
  navigateOnCreate: editorNavigateOnCreate,
  currentId,
  isNew,
  metadata,
  catalogMeta,
  docMeta,
  catalogItem,
  doc,
  docEffects,
  model,
  partsModel,
  loading,
  saving,
  canSave,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canDelete,
  canPost,
  canUnpost,
  isDirty,
  error,
  metaStore,
  lookupStore,
  initialFields: editorInitialFields,
  initialParts: editorInitialParts,
  currentEditorContext,
  resetInitialSnapshot,
  setEditorError,
  normalizeEditorError,
  emitCreated: (id) => emit('created', id),
  emitSaved: () => emit('saved'),
  emitChanged: (reason) => emit('changed', reason),
  emitDeleted: () => emit('deleted'),
  router,
  toasts,
}

const {
  load,
  save,
  markForDeletion,
  unmarkForDeletion,
  deleteEntity,
  post,
  unpost,
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
  canPost,
  canUnpost,
  isNew,
  isDirty,
  error,
  setEditorError,
  normalizeEditorError,
  emitChanged: (reason) => emit('changed', reason),
  emitDeleted: () => emit('deleted'),
  adapters: {
    catalog: useCatalogEntityEditorPersistence(persistenceContext),
    document: useDocumentEntityEditorPersistence(persistenceContext),
  },
})

function confirmMarkForDeletion() {
  markConfirmOpen.value = false
  void markForDeletion()
}

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
  buildCopyParts: () => clonePlainData(partsModel.value ?? null),
})

function toggleMarkForDeletion() {
  if (canUnmarkForDeletion.value) void unmarkForDeletion()
  else if (canMarkForDeletion.value) requestMarkForDeletion()
}

function togglePost() {
  if (canUnpost.value) void unpost()
  else if (canPost.value) void post()
}

const {
  extraPrimaryActions: configuredDocumentPrimaryActions,
  extraMoreActionGroups: configuredDocumentMoreActionGroups,
  handleConfiguredAction,
} = useConfiguredEntityEditorDocumentActions({
  kind: editorKind,
  typeCode: editorTypeCode,
  currentId: currentIdValue,
  model,
  uiEffects: docEffectsUi,
  loading: computed(() => loading.value),
  saving: computed(() => saving.value),
  requestNavigate,
  metadataStore: metaStore,
  setEditorError,
  normalizeEditorError,
})

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
  saving: computed(() => saving.value),
  isNew,
  isMarkedForDeletion,
  canSave,
  canPost,
  canUnpost,
  canMarkForDeletion,
  canUnmarkForDeletion,
  canOpenEffectsPage,
  canOpenDocumentFlowPage,
  canPrintDocument,
  canOpenAudit,
  canShareLink,
  onOpenCompactPage: openCompactPage,
  onOpenFullPage: openFullPage,
  onCopyDocument: copyDocument,
  onPrintDocument: openDocumentPrintPage,
  onToggleMarkForDeletion: toggleMarkForDeletion,
  onSave: save,
  onTogglePost: togglePost,
  onOpenEffectsPage: openDocumentEffectsPage,
  onOpenDocumentFlowPage: openDocumentFlowPage,
  onOpenAuditLog: openAuditLog,
  onCopyShareLink: copyShareLink,
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
  canOpenDocumentFlowPage,
  canOpenEffectsPage,
  canPrintDocument,
  canPost,
  canUnpost,
  openDocumentFlowPage,
  openDocumentEffectsPage,
  openDocumentPrintPage,
  post,
  unpost,
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
})

const pageActionHandlers = {
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
})

watch(
  () => [editorKind.value, editorTypeCode.value, currentId.value],
  () => void load(),
  { immediate: true },
)

const afterFormExtensions = computed<EntityEditorRenderExtension[]>(() => {
  if (!(editorKind.value === 'document' && (docMeta.value?.parts?.length ?? 0) > 0 && !loading.value)) {
    return []
  }

  return [{
    key: 'agency-billing-document-parts',
    component: AgencyBillingDocumentPartsEditor,
    props: {
      entityTypeCode: editorTypeCode.value,
      parts: docMeta.value?.parts ?? [],
      modelValue: partsModel.value,
      documentModel: model.value,
      'onUpdate:modelValue': (value: RecordPayload['parts'] | null) => {
        partsModel.value = value
      },
      readonly: isReadOnly.value,
      behavior: agencyBillingMetadataFormBehavior,
      errors: partErrors.value,
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

defineExpose(exposedHandle)
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
    :saving="saving"
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
    :dialog-extensions="[]"
    :audit-open="auditOpen"
    :audit-entity-kind="auditEntityKind"
    :audit-entity-id="auditEntityId"
    :audit-entity-title="auditEntityTitle"
    :leave-open="leaveOpen"
    :mark-confirm-open="markConfirmOpen"
    :mark-confirm-message="markConfirmMessage"
    @back="navigateBack(router, route, props.closeTo ?? fallbackCloseTarget)"
    @close="closePage"
    @action="handleHeaderAction"
    @closeAuditLog="closeAuditLog"
    @cancelLeave="cancelLeave"
    @confirmLeave="confirmLeave"
    @cancelMarkForDeletion="cancelMarkForDeletion"
    @confirmMarkForDeletion="confirmMarkForDeletion"
  />
</template>
