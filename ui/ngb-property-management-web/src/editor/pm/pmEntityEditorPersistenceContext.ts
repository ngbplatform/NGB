import type { ComputedRef, Ref } from 'vue'
import type { Router } from 'vue-router'
import type {
  CatalogItemDto,
  CatalogTypeMetadataDto,
  DocumentDto,
  DocumentEffectsDto,
  DocumentTypeMetadataDto,
  EditorChangeReason,
  EditorErrorState,
  EntityEditorContext,
  EntityEditorMetadataStoreLike,
  EntityEditorToastApi,
  EntityFormModel,
  EditorKind,
  EditorMode,
  LookupStoreApi,
  RecordPayload,
} from 'ngb-ui-framework'
import type { LeasePartyRow } from './leasePartyTypes'

export type PmEntityEditorLeaseAdapter = {
  isLeaseDocument: ComputedRef<boolean>
  leasePartiesRows: Ref<LeasePartyRow[]>
  ensureLeasePartiesInitialized: () => void
  validateLeasePartiesBeforeSave: () => string | null
  applyInitialParts: (parts: RecordPayload['parts'] | null | undefined) => void
  applyPersistedParts: (parts: RecordPayload['parts'] | null | undefined) => void
  buildSaveParts: () => RecordPayload['parts'] | undefined
}

export type PmEntityEditorPersistenceContext = {
  kind: ComputedRef<EditorKind>
  typeCode: ComputedRef<string>
  mode: ComputedRef<EditorMode>
  navigateOnCreate: ComputedRef<boolean | undefined>
  currentId: Ref<string | null>
  isNew: ComputedRef<boolean>
  metadata: ComputedRef<CatalogTypeMetadataDto | DocumentTypeMetadataDto | null>
  catalogMeta: Ref<CatalogTypeMetadataDto | null>
  docMeta: Ref<DocumentTypeMetadataDto | null>
  catalogItem: Ref<CatalogItemDto | null>
  doc: Ref<DocumentDto | null>
  docEffects: Ref<DocumentEffectsDto | null>
  model: Ref<EntityFormModel>
  loading: Ref<boolean>
  saving: Ref<boolean>
  canSave: ComputedRef<boolean>
  canMarkForDeletion: ComputedRef<boolean>
  canUnmarkForDeletion: ComputedRef<boolean>
  canDelete: ComputedRef<boolean>
  canPost: ComputedRef<boolean>
  canUnpost: ComputedRef<boolean>
  isDirty: ComputedRef<boolean>
  error: Ref<EditorErrorState | null>
  metaStore: EntityEditorMetadataStoreLike<CatalogTypeMetadataDto, DocumentTypeMetadataDto>
  lookupStore: LookupStoreApi
  initialFields: ComputedRef<EntityFormModel | null>
  initialParts: ComputedRef<RecordPayload['parts'] | null>
  leaseEditor: PmEntityEditorLeaseAdapter
  currentEditorContext: () => EntityEditorContext
  resetInitialSnapshot: () => void
  setEditorError: (value: EditorErrorState | null) => void
  normalizeEditorError: (cause: unknown) => EditorErrorState
  emitCreated: (id: string) => void
  emitSaved: () => void
  emitChanged: (reason?: EditorChangeReason) => void
  emitDeleted: () => void
  router: Router
  toasts: EntityEditorToastApi
}
