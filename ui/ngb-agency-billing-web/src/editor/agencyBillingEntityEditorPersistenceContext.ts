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
  EditorKind,
  EditorMode,
  EntityEditorContext,
  EntityFormModel,
  RecordPayload,
} from 'ngb-ui-framework'
import { useLookupStore, useMetadataStore, useToasts } from 'ngb-ui-framework'

export type AgencyBillingEntityEditorPersistenceContext = {
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
  partsModel: Ref<RecordPayload['parts'] | null>
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
  metaStore: ReturnType<typeof useMetadataStore>
  lookupStore: ReturnType<typeof useLookupStore>
  initialFields: ComputedRef<EntityFormModel | null | undefined>
  initialParts: ComputedRef<RecordPayload['parts'] | null | undefined>
  currentEditorContext: () => EntityEditorContext
  resetInitialSnapshot: () => void
  setEditorError: (value: EditorErrorState | null) => void
  normalizeEditorError: (cause: unknown) => EditorErrorState
  emitCreated: (id: string) => void
  emitSaved: () => void
  emitChanged: (reason?: EditorChangeReason) => void
  emitDeleted: () => void
  router: Router
  toasts: ReturnType<typeof useToasts>
}
